using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using OrderApiMonolith.Data;
using OrderApiMonolith.Data.Repositories;
using OrderApiMonolith.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MassTransit;
using OrderApiCQRS.Consumers.CommandHandlers;
using GreenPipes;
using OrderApiCQRS.Consumers.EventHandlers;

namespace OrderApiMonolith
{
    public class Startup
    {
        private IConfiguration Configuration { get; }

        public Startup(IWebHostEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", false, true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();
        }


        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "OrderAPI - CQRS", Version = "v1" });
            });

            services.AddControllers();

            services.AddDbContext<WorkshopDbContext>(options =>
            {
                options.UseSqlServer(Configuration.GetConnectionString("WorkshopDb"),
                    sqlOptions =>
                    {
                        sqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: System.TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
                    });
            });

            services.AddMassTransit(x =>
            {
                x.AddConsumer<CreateOrderCommandHandler>();
                x.AddConsumer<OrderCreatedEventHandler>();
                x.AddConsumer<CalculateDailyTotalSalesHandler>();

                x.AddBus(context => Bus.Factory.CreateUsingRabbitMq(cfg =>
                {
                    cfg.UseHealthCheck(context);

                    cfg.Host("rabbitmq://localhost");

                    cfg.ReceiveEndpoint("create-order-command-queue", ep =>
                    {
                        ep.PrefetchCount = 16;
                        ep.UseMessageRetry(r => r.Interval(3, 500));
                        ep.ConfigureConsumer<CreateOrderCommandHandler>(context);
                    });

                    cfg.ReceiveEndpoint("order-created-event-queue", ep =>
                    {
                        ep.PrefetchCount = 16;
                        ep.UseMessageRetry(r => r.Interval(3, 500));
                        ep.ConfigureConsumer<OrderCreatedEventHandler>(context);
                    });

                    cfg.ReceiveEndpoint("daily-total-sales-queue", ep =>
                    {
                        ep.PrefetchCount = 16;
                        ep.UseMessageRetry(r => r.Interval(3, 500));
                        ep.ConfigureConsumer<CalculateDailyTotalSalesHandler>(context);
                    });
                }));
            });

            services.AddMassTransitHostedService();

            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<IOrderRepository, OrderRepository>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapDefaultControllerRoute());

            app.UseSwagger();
            app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "OrderAPI - CQRS V1"); });

            var serviceScopeFactory = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>();
            using (var serviceScope = serviceScopeFactory.CreateScope())
            {
                var dbContext = serviceScope.ServiceProvider.GetService<WorkshopDbContext>();
                dbContext.Database.EnsureCreated();
            }
        }
    }
}