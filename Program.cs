using Discord.WebSocket;
using RoganBot;
using RoganBot.Models;
using RoganBot.Service;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((builder, services) =>
    {
        var botSettings = builder.Configuration.GetSection(nameof(RoganBotSettings));
        RoganBotSettings? roganBotSettings = botSettings.Get<RoganBotSettings>();
 

        services.AddLogging()
                .Configure<RoganBotSettings>(botSettings)
                .AddSingleton<DiscordSocketClient>(DiscordServicesFactory.NewDiscordSocketClient(roganBotSettings))
                .AddSingleton(DiscordServicesFactory.CreateCommandService(roganBotSettings))
                .AddHostedService<RoganBotService>();
    })
    .Build();
host.Run();