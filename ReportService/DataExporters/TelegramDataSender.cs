﻿using System;
using Autofac;
using Newtonsoft.Json;
using ReportService.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace ReportService.DataExporters
{
    public class TelegramDataSender : CommonDataExporter
    {
        private readonly ITelegramBotClient bot;
        private readonly DtoTelegramChannel channel;
        private readonly IViewExecutor viewExecutor;
        private readonly string reportName;

        public TelegramDataSender(ITelegramBotClient botClient, ILifetimeScope autofac,
                                  ILogic logic, string jsonConfig)
        {
            var config = JsonConvert
                .DeserializeObject<TelegramExporterConfig>(jsonConfig);

            Number = config.Number;
            DataSetName = config.DataSetName;
            channel = logic.GetTelegramChatIdByChannelId(config.TelegramChannelId);
            reportName = config.ReportName;
            viewExecutor = autofac.ResolveNamed<IViewExecutor>("commonviewex");
            bot = botClient;
        }

        public override void Send(string dataSet)
        {
            try
            {
                bot.SendTextMessageAsync(channel.ChatId,
                        viewExecutor.ExecuteTelegramView(dataSet, reportName),
                        ParseMode.Markdown)
                    .Wait();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}