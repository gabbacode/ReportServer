﻿using System;
using System.Configuration;
using System.Linq;
using System.Net;
using Autofac;
using AutoMapper;
using Domain0.Tokens;
using Monik.Client;
using Monik.Common;
using Nancy;
using Nancy.Bootstrapper;
using Nancy.Bootstrappers.Autofac;
using Nancy.Conventions;
using Nancy.Swagger.Annotations;
using Nancy.Swagger.Services;
using Newtonsoft.Json;
using ReportService.Core;
using ReportService.Entities;
using ReportService.Extensions;
using ReportService.Interfaces.Core;
using ReportService.Interfaces.Operations;
using ReportService.Interfaces.Protobuf;
using ReportService.Interfaces.ReportTask;
using ReportService.Nancy;
using ReportService.Nancy.Models;
using ReportService.Operations.DataExporters;
using ReportService.Operations.DataExporters.Configurations;
using ReportService.Operations.DataExporters.ViewExecutors;
using ReportService.Operations.DataImporters;
using ReportService.Operations.DataImporters.Configurations;
using ReportService.Protobuf;
using ReportService.ReportTask;
using Swagger.ObjectModel;
using Telegram.Bot;

namespace ReportService
{
    public interface IPrivateBootstrapper
    {
        void PrivateConfigureApplicationContainer(ILifetimeScope existingContainer);
    }

    public partial class Bootstrapper : AutofacNancyBootstrapper
    {
        public static ILifetimeScope Global; //mech of work?

        public ILifetimeScope Container => ApplicationContainer;

        protected override void ApplicationStartup(ILifetimeScope container, IPipelines pipelines)
        {
            SwaggerMetadataProvider.SetInfo("Reporting service", "v2", "Reporting service docs", new Contact()
            {
                Name = "Reportserver"
            });

            container.Update(builder => builder
                .RegisterType<SwaggerAnnotationsProvider>()
                .As<ISwaggerMetadataProvider>());
            Global = Container;
            ILogic log = Container.Resolve<ILogic>();
            log.Start();

            SwaggerAnnotationsConfig.ShowOnlyAnnotatedRoutes = true;
        }

        protected override void ConfigureConventions(NancyConventions nancyConventions)
        {
            base.ConfigureConventions(nancyConventions);
        
            nancyConventions.StaticContentsConventions
                .AddEmbeddedDirectory<Bootstrapper>("/swagger-ui", "Nancy/SwaggerDist");
        }

        protected override void ConfigureApplicationContainer(ILifetimeScope existingContainer)
        {
            RegisterNamedDataImporter<DbImporter, DbImporterConfig>
                (existingContainer, "CommonDbImporter");

            RegisterNamedDataImporter<ExcelImporter, ExcelImporterConfig>
                (existingContainer, "CommonExcelImporter");

            RegisterNamedDataImporter<CsvImporter, CsvImporterConfig>
                (existingContainer, "CommonCsvImporter");

            RegisterNamedDataImporter<SshImporter, SshImporterConfig>
                (existingContainer, "CommonSshImporter");
            
            RegisterNamedDataExporter<EmailDataSender, EmailExporterConfig>
                (existingContainer, "CommonEmailSender");

            RegisterNamedDataExporter<TelegramDataSender, TelegramExporterConfig>
                (existingContainer, "CommonTelegramSender");

            RegisterNamedDataExporter<DbExporter, DbExporterConfig>
                (existingContainer, "CommonDbExporter");

            RegisterNamedDataExporter<B2BExporter, B2BExporterConfig>
                (existingContainer, "CommonB2BExporter");

            RegisterNamedDataExporter<SshExporter, SshExporterConfig>
                (existingContainer, "CommonSshExporter");

            RegisterNamedDataExporter<FtpExporter, FtpExporterConfig>
                (existingContainer, "CommonFtpExporter");

            RegisterNamedViewExecutor<CommonViewExecutor>
                (existingContainer, "commonviewex");

            RegisterNamedViewExecutor<GroupedViewExecutor>
                (existingContainer, "GroupedViewex");

            RegisterNamedViewExecutor<CommonTableViewExecutor>
            (existingContainer,"CommonTableViewEx");

            existingContainer
                .RegisterImplementationSingleton<ILogic, Logic>();
            existingContainer
                .RegisterImplementation<IReportTask, ReportTask.ReportTask>();

            // Partial bootstrapper for private named implementations registration
            (this as IPrivateBootstrapper)?
                .PrivateConfigureApplicationContainer(existingContainer);

            #region ConfigureMonik

            var logSender = new AzureSender(
                ConfigurationManager.AppSettings["EndPoint"],
                "incoming");

            existingContainer
                .RegisterInstance<IMonikSender, AzureSender>(logSender);

            var monikSettings = new ClientSettings
            {
                SourceName = "ReportServer",
                InstanceName = ConfigurationManager.AppSettings["InstanceName"],
                AutoKeepAliveEnable = true
            };

            existingContainer
                .RegisterInstance<IMonikSettings, ClientSettings>(monikSettings);

            existingContainer
                .RegisterImplementationSingleton<IMonik, MonikClient>();

            #endregion

            var repository = new Repository(ConfigurationManager.AppSettings["DBConnStr"],
                existingContainer.Resolve<IMonik>());

            existingContainer
                .RegisterInstance<IRepository, Repository>(repository);

            #region ConfigureMapper

            var mapperConfig =
                new MapperConfiguration(cfg => cfg.AddProfile(typeof(MapperProfile)));

            // Hint: add to ctor if many profiles needed: cfg.AddProfile(typeof(AutoMapperProfile));
            existingContainer.RegisterSingleInstance<MapperConfiguration, MapperConfiguration>(
                mapperConfig);
            var mapper = existingContainer.Resolve<MapperConfiguration>().CreateMapper();
            existingContainer.RegisterSingleInstance<IMapper, IMapper>(mapper);

            #endregion

            existingContainer.RegisterImplementation<IDefaultTaskExporter, DefaultTaskExporter>();


            existingContainer.RegisterNamedImplementation<IArchiver, ArchiverZip>("Zip");
            existingContainer.RegisterNamedImplementation<IArchiver, Archiver7Zip>("7Zip");

            switch (ConfigurationManager.AppSettings["ArchiveFormat"])
            {
                case "Zip":
                    existingContainer.RegisterImplementation<IArchiver, ArchiverZip>();
                    break;
                case "7Zip":
                    existingContainer.RegisterImplementation<IArchiver, Archiver7Zip>();
                    break;
            }


            existingContainer.RegisterImplementation<IReportTaskRunContext, ReportTaskRunContext>();

            existingContainer.RegisterImplementation<ITaskWorker, TaskWorker>();

            existingContainer.RegisterImplementation<IPackageBuilder, ProtoPackageBuilder>();
            existingContainer.RegisterImplementation<IPackageParser, ProtoPackageParser>();

            existingContainer.RegisterImplementation<IProtoSerializer, ProtoSerializer>();

            #region ConfigureBot

            Uri proxyUri = new Uri(ConfigurationManager.AppSettings["ProxyUriAddr"]);
            ICredentials credentials = new NetworkCredential(
                ConfigurationManager.AppSettings["ProxyLogin"],
                ConfigurationManager.AppSettings["ProxyPassword"]);
            WebProxy proxy = new WebProxy(proxyUri, true, null, credentials);
            TelegramBotClient bot =
                new TelegramBotClient(ConfigurationManager.AppSettings["BotToken"], proxy);
            existingContainer
                .RegisterSingleInstance<ITelegramBotClient, TelegramBotClient>(bot);

            #endregion

            existingContainer //why?
                .RegisterInstance<ILifetimeScope, ILifetimeScope>(existingContainer);

            existingContainer.RegisterInstance<TokenValidationSettings, TokenValidationSettings>(
                new TokenValidationSettings
                {
                    Audience = ConfigurationManager.AppSettings["Token_Audience"],
                    Issuer = ConfigurationManager.AppSettings["Token_Issuer"],
                    Keys = new[]
                    {
                        new KeyInfo
                        {
                            Key = ConfigurationManager.AppSettings["Token_Secret"],
                            Alg = ConfigurationManager.AppSettings["Token_Alg"]
                        }
                    }
                });
        }


        protected override void ConfigureRequestContainer(ILifetimeScope container,
            NancyContext context)
        {
            // Perform registrations that should have a request lifetime
        }

        protected override void RequestStartup(ILifetimeScope container, IPipelines pipelines,
            NancyContext context)
        {
            base.RequestStartup(container, pipelines, context);

            pipelines.AddDomain0Auth(container
                .Resolve<TokenValidationSettings>());
        }

        private void RegisterNamedViewExecutor<TImplementation>
            (ILifetimeScope container, string name) where TImplementation : IViewExecutor
        {
            container.Update(builder => builder
                .RegisterType<TImplementation>()
                .Named<IViewExecutor>(name));
        }

        private void RegisterNamedDataExporter<TImplementation, TConfigType>
            (ILifetimeScope container, string name)
            where TImplementation : IOperation
            where TConfigType : IExporterConfig
        {
            container.Update(builder => builder
                .RegisterType<TImplementation>()
                .Named<IOperation>(name)
                .Keyed<IOperation>(typeof(TConfigType)));
        }

        private void RegisterNamedDataImporter<TImplementation, TConfigType>
            (ILifetimeScope container, string name)
            where TImplementation : IOperation
            where TConfigType : IImporterConfig
        {
            container.Update(builder => builder
                .RegisterType<TImplementation>()
                .Named<IOperation>(name)
                .Keyed<IOperation>(typeof(TConfigType)));
        }
    }

    public class MapperProfile : Profile
    {
        public MapperProfile()
        {
            CreateMap<DtoRecepientGroup, RecipientGroup>();

            CreateMap<ReportTask.ReportTask, DtoTask>()
                .ForMember("ScheduleId", opt => opt.MapFrom(s => s.Schedule.Id))
                .ForMember("Parameters", opt =>
                    opt.MapFrom(s => JsonConvert.SerializeObject(s.Parameters)))
                .ForMember("DependsOn", opt =>
                    opt.MapFrom(s => JsonConvert.SerializeObject(s.DependsOn)));

            CreateMap<ReportTask.ReportTask, ApiTask>()
                .ForMember("ScheduleId", opt => opt.MapFrom(s => s.Schedule.Id))
                .ForMember("Parameters", opt =>
                    opt.MapFrom(s => JsonConvert.SerializeObject(s.Parameters)));

            CreateMap<ApiTask, DtoTask>()
                .ForMember("DependsOn", opt =>
                    opt.MapFrom(s =>
                        s.DependsOn == null
                            ? null
                            : JsonConvert.SerializeObject(s.DependsOn)));

            CreateMap<DtoOperInstance, ApiOperInstance>()
                .ForMember("DataSet", opt => opt.Ignore());

            CreateMap<DtoOperInstance, DtoTaskInstance>();

            CreateMap<DtoOperation, CommonOperationProperties>();

            CreateMap<DbExporterConfig, DbExporter>();
            CreateMap<DbExporterConfig, CommonOperationProperties>();
            CreateMap<EmailExporterConfig, EmailDataSender>();
            CreateMap<EmailExporterConfig, CommonOperationProperties>();
            CreateMap<TelegramExporterConfig, TelegramDataSender>();
            CreateMap<TelegramExporterConfig, CommonOperationProperties>();
            CreateMap<B2BExporterConfig, B2BExporter>();
            CreateMap<B2BExporterConfig, CommonOperationProperties>();
            CreateMap<DbImporterConfig, DbImporter>()
                .ForMember("DataSetNames", opt =>
                    opt.MapFrom(s => s.DataSetNames.Split(new[] {';'},
                            StringSplitOptions.RemoveEmptyEntries)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .ToList()));
            CreateMap<DbImporterConfig, CommonOperationProperties>();
            CreateMap<ExcelImporterConfig, ExcelImporter>();
            CreateMap<ExcelImporterConfig, CommonOperationProperties>();
            CreateMap<ExcelImporterConfig, ExcelReadingParameters>();
            CreateMap<ExcelImporterConfig, CommonOperationProperties>();
            CreateMap<CsvImporterConfig, CsvImporter>();
            CreateMap<CsvImporterConfig, CommonOperationProperties>();
            CreateMap<SshImporterConfig, SshImporter>();
            CreateMap<SshImporterConfig, CommonOperationProperties>();
            CreateMap<SshExporterConfig, SshExporter>();
            CreateMap<SshExporterConfig, CommonOperationProperties>();
            CreateMap<FtpExporterConfig, FtpExporter>();
            CreateMap<FtpExporterConfig, CommonOperationProperties>();
            CreateMap<HistoryImporterConfig, HistoryImporter>();
            CreateMap<HistoryImporterConfig, CommonOperationProperties>();
        }
    }
}