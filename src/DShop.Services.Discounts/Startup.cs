﻿using System;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Consul;
using DShop.Common;
using DShop.Common.Consul;
using DShop.Common.Dispatchers;
using DShop.Common.Mongo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DShop.Common.Mvc;
using DShop.Common.RabbitMq;
using DShop.Common.RestEase;
using DShop.Services.Discounts.Domain;
using DShop.Services.Discounts.Messages.Commands;
using DShop.Services.Discounts.Messages.Events;
using DShop.Services.Discounts.Services;

namespace DShop.Services.Discounts
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        public IContainer Container { get; private set; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddCustomMvc();
            services.AddInitializers(typeof(IMongoDbInitializer));
            services.AddConsul();
            services.RegisterServiceForwarder<IOrdersService>("orders-service");
            
            var builder = new ContainerBuilder();
            builder.RegisterAssemblyTypes(Assembly.GetEntryAssembly())
                .AsImplementedInterfaces();
            builder.Populate(services);
            builder.AddDispatchers();
            builder.AddMongo();
            builder.AddMongoRepository<Customer>("Customers");
            builder.AddMongoRepository<Discount>("Discounts");
            builder.AddRabbitMq();

            Container = builder.Build();
            
            return new AutofacServiceProvider(Container);
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env,
            IApplicationLifetime applicationLifetime, IStartupInitializer initializer,
            IConsulClient consulClient)
        {
            if (env.IsDevelopment() || env.EnvironmentName == "local")
            {
                app.UseDeveloperExceptionPage();
            }

            initializer.InitializeAsync();
            app.UseMvc();
            app.UseRabbitMq()
                .SubscribeCommand<CreateDiscount>()
                .SubscribeEvent<CustomerCreated>(@namespace: "customers")
                .SubscribeEvent<OrderCompleted>(@namespace: "orders");
            var serviceId = app.UseConsul();

            applicationLifetime.ApplicationStopped.Register(() =>
            {
                consulClient.Agent.ServiceDeregister(serviceId);
                Container.Dispose();
            });
        }
    }
}
