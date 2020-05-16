﻿using Autofac;
using ClipboardManager.models;

namespace Components
{
    public static class AppModule
    {
        public static IContainer Container;
        public static IContainer Configure()
        {
            var builder = new ContainerBuilder();
            builder.RegisterType<ClipboardUtlity>().As<IClipboardUtlity>();
            builder.RegisterType<DatabaseHelper>().As<IDatabase<TableCopy>>();
            builder.RegisterType<ClipboardService>().As<IKeyboardRecorder>().SingleInstance();

            return builder.Build().Also((e)=> Container = e);
        }

    }
}
