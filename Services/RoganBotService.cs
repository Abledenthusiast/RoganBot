using Discord;
using Discord.Audio;
using Discord.Audio.Streams;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using RoganBot.Models;
using System.Diagnostics;
using System.Globalization;
using Game = Discord.Game;
using Timer = System.Timers.Timer;

namespace RoganBot.Service
{
    public class RoganBotService : BackgroundService
    {
        private readonly IServiceProvider _provider;
        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;
        private readonly RoganBotSettings _settings;
        private readonly ILogger<RoganBotService> _logger;
        private readonly CommandHandler _handler;


        public RoganBotService(IServiceProvider serviceProvider,
                               IOptions<RoganBotSettings> botConfig,
                               ILogger<RoganBotService> logger,
                               DiscordSocketClient? client,
                               CommandService? commands)
        {
            _provider = serviceProvider;
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _settings = botConfig.Value ?? throw new ArgumentNullException(nameof(botConfig));
            _logger = logger;
            _handler = new CommandHandler(_client, _commands, _settings, _logger);

            _logger.LogInformation($"Bot token available? {!string.IsNullOrEmpty(_settings.Token)}");
            _logger.LogInformation($"commands available? {commands != null}");
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await base.StartAsync(cancellationToken);

            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-us");
            Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture;

            try       
            {
                cancellationToken.ThrowIfCancellationRequested();

                _handler.Initialize(_provider);

                 _logger?.LogInformation("Setting up discord bot for execution");

                await _commands.AddModulesAsync(typeof(RoganBotService).Assembly, _provider);
                await _client.LoginAsync(TokenType.Bot, _settings.Token);
                await _client.StartAsync();
            }
            catch (Exception e)
            {
                _logger.LogError($"Could not connect to Discord. Exception: {e.Message}");
                throw;
            }
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;


        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _client.LogoutAsync();
            _logger.LogInformation("Discord bot stopped");
        }


        public override void Dispose()
        {
            _client.Dispose();
            ((IDisposable)_commands).Dispose();
            base.Dispose();
        }

        internal class CommandHandler
        {

            private static readonly int SOUND_BYTE_CACHE_MAX_COUNT = 20;
            private static readonly double FIVE_MINUTES = 300_000;

            private readonly DiscordSocketClient _client;
            private readonly CommandService _commands;
            private readonly RoganBotSettings _settings;
            private readonly ILogger<RoganBotService> _logger;
            private readonly Dictionary<IVoiceChannel, VoiceChannelInfo> connectedVoiceChannels;

            private IServiceProvider? _provider;
            private Dictionary<string, RoganResponse>? defaultResponses;
            private Timer disconnectTimer;
            private Dictionary<string, byte[]> soundByteCache;

            internal CommandHandler(DiscordSocketClient client, 
                                  CommandService commands, 
                                  RoganBotSettings settings, 
                                  ILogger<RoganBotService> logger)
            {
                _client = client;
                _commands = commands;
                _settings = settings;
                _logger = logger;
                connectedVoiceChannels = new Dictionary<IVoiceChannel, VoiceChannelInfo>();
                soundByteCache = new Dictionary<string, byte[]>();

                // Starts the rolling timer to disconnect inactive voice channels
                // 
                StartTimer();
            }

            private void StartTimer()
            {
                // Every 5 min check connected channels
                //
                disconnectTimer = new Timer(FIVE_MINUTES);

                // Set a timer to remove the bot from the channel
                //
                disconnectTimer.Elapsed += (source, eventArgs) => DisconnectVoiceChannels();
                disconnectTimer.Start();
            }

            internal void Initialize(IServiceProvider provider)
            {
                _provider = provider ?? throw new ArgumentNullException(nameof(provider));
                defaultResponses = _settings.AOEResponses;
                BindEvents();
            }

            private void BindEvents()
            {
                _client.Ready += HandleReady;
                _client.Log += LogAsync;
                _client.MessageReceived += HandleCommandAsync;
            }

            internal void UnbindEvents()
            {
                _client.Ready -= HandleReady;
                _client.Log -= LogAsync;
                _client.MessageReceived -= HandleCommandAsync;
            }
            
            private Task LogAsync(LogMessage log)
            {
                _logger.LogInformation(log.Message);
                return Task.CompletedTask;
            }

            private async Task HandleReady()
            {
                await _client.SetActivityAsync(new Game("Age of Empires 2"));
            }

            private async Task HandleCommandAsync(SocketMessage message)
            {
                _logger.LogInformation($"Message received from channel: {message.Channel.ToString()}");

                // only allow users to make use of commands
                //
                if (message is not SocketUserMessage userMessage || userMessage.Author.IsBot)
                    return;

                await HandleMessageAsync(userMessage);
            }

            private async Task HandleMessageAsync(SocketUserMessage userMessage)
            {
                int argumentPosition = 0;

                // set up the context used for commands
                //
                var context = new SocketCommandContext(_client, userMessage);

                // Check if there is an AOE2 taunt response for this message.
                //
                var aoe2Response = defaultResponses?.GetValueOrDefault(userMessage.CleanContent.Trim(), null);

                _logger.LogInformation($"Message received from user: {userMessage.CleanContent}");
                _logger.LogInformation($"Response found: {aoe2Response}");

                if (aoe2Response != null)
                {
                    // Fire and forget. Don't block the current thread.
                    //
                    RespondToAgeOfEmpiresTauntAsync(context, aoe2Response);
                    return;
                }

                // No commands have been added so far.
                //
                if (!_settings.CommandsEnabled)
                {
                    return;
                }

                // determines if the message has the determined prefix
                //
                if (!userMessage.HasStringPrefix(_settings.Prefix, ref argumentPosition) && !(_settings.UseMentionPrefix && userMessage.HasMentionPrefix(_client.CurrentUser, ref argumentPosition)))
                    return;

                // execute the command
                //
                var result = await _commands.ExecuteAsync(context, argumentPosition, _provider);

                if (!result.IsSuccess && result.Error != CommandError.UnknownCommand && !string.IsNullOrWhiteSpace(result.ErrorReason))
                    await context.Channel.SendMessageAsync(result.ErrorReason);
            }

            private async Task RespondToAgeOfEmpiresTauntAsync(SocketCommandContext context, RoganResponse? aoe2Response)
            {
                // If there's an audio channel to respond to, do that.
                // Otherwise fallback to text.
                //
                var channel = (context.User as IGuildUser)?.VoiceChannel;

                if (channel == null || string.IsNullOrEmpty(aoe2Response.FileName))
                {
                    await context.Channel.SendMessageAsync(aoe2Response.TextResponse);
                }
                else
                {
                    _logger.LogInformation(@$"User is in voice channel ""{channel.Name}"" in {channel.Guild} found");

                    // This is fire and forget. Don't block the current thread handling incoming messages.
                    //
                    _ = Task.Run(async () =>
                    {
                        await ConnectToVoiceChannel(channel);
                        await SendAsync(channel, @$"Sounds\{aoe2Response.FileName}");
                    });
                }
            }

            private async Task ConnectToVoiceChannel(IVoiceChannel channel)
            {
                if (!connectedVoiceChannels.ContainsKey(channel) || connectedVoiceChannels[channel].Item1.ConnectionState == ConnectionState.Disconnected)
                {
                    IAudioClient audioClient = await channel.ConnectAsync();
                    audioClient.Disconnected += (exception) =>
                    {
                        connectedVoiceChannels.Remove(channel);
                        return Task.CompletedTask;
                    };

                    // PCMStream does not get disposed of correctly. So just keep it around for future use.
                    //
                    var audioOutStream = audioClient.CreatePCMStream(AudioApplication.Voice, 92160, 15);
                    connectedVoiceChannels.Add(channel, (audioClient, audioOutStream, DateTime.Now));
                }
            }

            private void DisconnectVoiceChannels()
            {
                foreach(var channel in connectedVoiceChannels)
                {
                    // if it's been more than 20 minutes since the bot was last used
                    // remove it from the active voice channels and disconnect.
                    //
                    if (DateTime.Now - channel.Value.Item3 >= TimeSpan.FromMinutes(20))
                    {
                        _logger.LogInformation($"Leaving channel {channel.Key.Name}");
                        connectedVoiceChannels.Remove(channel.Key);
                        channel.Key.DisconnectAsync();
                    }
                }
            }


            private async Task SendAsync(IVoiceChannel channel, string path)
            {
                var connectedChannel = connectedVoiceChannels[channel];
                var audioClient = connectedChannel.Item1;
                var audioOutStream = connectedChannel.Item2;

                try
                {
                    // Set speaking indicator
                    //
                    await audioClient.SetSpeakingAsync(true).ConfigureAwait(false);

                    _logger.LogInformation($"Sending audio via {audioClient}");
                    try
                    {
                        // Get the raw bytes of the sound effect
                        //
                        byte[] bytes = getSoundBytes(path);

                        // lock on the audioOutStream so that if there are many concurrent triggering
                        // messages, they won't step on each other and sound terrible in the channel
                        //
                        // This could also cause an AV it seems if both threads try to call CopyTo on
                        // same out stream at the same time.
                        //
                        lock (audioOutStream)
                        {
                            audioOutStream.Write(bytes, 0, bytes.Length);

                            // This seems to do nothing.
                            //
                            audioOutStream.Flush();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex.Message);
                        _logger.LogError($"- {ex.StackTrace}");
                    }
                    finally
                    {
                        _logger.LogInformation("Done.");
                    }

                    // Turn off speaking indicator
                    //
                    await audioClient.SetSpeakingAsync(false).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    Console.WriteLine($"- {ex.StackTrace}");
                }
            }

            private byte[] getSoundBytes(string path)
            {
                // The result from ffmpeg is cached. The files are small enough, so 130k
                //
                if (soundByteCache.ContainsKey(path))
                {
                    return soundByteCache[path];
                }

                // This should really be a LRU cache...
                // This is simple enough, but I'm not going to
                // do it at the moment.
                //
                if (soundByteCache.Count >= SOUND_BYTE_CACHE_MAX_COUNT)
                {
                    soundByteCache.Clear();
                }

                using (var ffmpeg = CreateProcess(path))
                using (var memoryStream = new MemoryStream())
                {
                    ffmpeg.StandardOutput.BaseStream.CopyTo(memoryStream);
                    byte[] soundBytes = memoryStream.ToArray();
                    soundByteCache[path] = soundBytes;
                }

                return soundByteCache[path];
            }

            private Process CreateProcess(string path)
            {
                return Process.Start(new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = @$"-i ""{path}"" -ac 2 -f s16le -ar 48000 pipe:1",
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                });
            }

        }

    }

    internal record struct VoiceChannelInfo(IAudioClient Item1, AudioOutStream Item2, DateTime Item3)
    {
        public static implicit operator (IAudioClient, AudioOutStream, DateTime)(VoiceChannelInfo value)
        {
            return (value.Item1, value.Item2, value.Item3);
        }

        public static implicit operator VoiceChannelInfo((IAudioClient, AudioOutStream, DateTime) value)
        {
            return new VoiceChannelInfo(value.Item1, value.Item2, value.Item3);
        }
    }
}
