using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using Discord.Audio;

class Program
{
    private DiscordSocketClient _client;
    private Dictionary<ulong, Queue<SongInfo>> _queue;
    private string _prefix = "!";
    private string _token = "MTE5NDc0NjAyNTQ4NzkxMzA4MQ.G7oWjX.WoXF7VliplQAXD9w3ik2GgA6-WKl75XEteKNwY";

    public static void Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

        new Program().MainAsync().GetAwaiter().GetResult();
    }

    public async Task MainAsync()
    {
        _client = new DiscordSocketClient();
        _queue = new Dictionary<ulong, Queue<SongInfo>>();

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.MessageReceived += MessageReceivedAsync;

        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();

        await Task.Delay(-1);
    }

    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }

    private Task ReadyAsync()
    {
        Console.WriteLine("Ready!");
        return Task.CompletedTask;
    }

    private async Task MessageReceivedAsync(SocketMessage messageParam)
    {
        var message = messageParam as SocketUserMessage;
        var context = new SocketCommandContext(_client, message);

        if (message.Author.IsBot || !message.Content.StartsWith(_prefix))
            return;

        var argPos = 0;
        if (!message.HasStringPrefix(_prefix, ref argPos))
            return;

        var serverQueue = GetOrCreateQueue(context.Guild.Id);

        var command = message.Content.Split(' ')[0].ToLower();

        if (command == "play")
        {
            await ExecuteAsync(context, serverQueue);
        }
        else if (command == "stop")
        {
            Stop(context, serverQueue);
        }
        else if (command == "next")
        {
            Next(context, serverQueue);
        }
        else if (command == "help")
        {
            Help(context);
        }
        else if (command == "clear")
        {
            Clear(context, message.Content.Split(' ')[1]);
        }
    }

    private Queue<SongInfo> GetOrCreateQueue(ulong guildId)
    {
        if (!_queue.TryGetValue(guildId, out var queue))
        {
            queue = new Queue<SongInfo>();
            _queue[guildId] = queue;
        }
        return queue;
    }

    private async Task ExecuteAsync(SocketCommandContext context, Queue<SongInfo> serverQueue)
    {
        var args = context.Message.Content.Split(' ');

        if (args.Length < 2)
        {
            await context.Channel.SendMessageAsync("Пожалуйста, предоставьте действительный URL песни!");
            return;
        }

        var voiceChannel = (context.User as IGuildUser)?.VoiceChannel;
        if (voiceChannel == null)
        {
            await context.Channel.SendMessageAsync("Вы должны быть в голосовом канале, чтобы воспроизводить музыку!");
            return;
        }

        var songInfo = await new YoutubeClient().Videos.GetAsync(args[1]);
        var song = new SongInfo
        {
            Title = songInfo.Title,
            Url = songInfo.Url
        };

        if (serverQueue.Count == 0)
        {
            serverQueue.Enqueue(song);

            try
            {
                var connection = await voiceChannel.ConnectAsync();
                await PlayAsync(context.Guild, serverQueue.Peek(), connection);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                _queue.Remove(context.Guild.Id);
                await context.Channel.SendMessageAsync(ex.Message);
            }
        }
        else
        {
            serverQueue.Enqueue(song);
            await context.Channel.SendMessageAsync($"{song.Title} добавлен в очередь!");
        }
    }

    private async Task PlayAsync(IGuild guild, SongInfo song, IAudioClient connection)
    {
        if (song == null)
        {
            await connection.StopAsync();
            _queue.Remove(guild.Id);
            return;
        }

        var youtube = new YoutubeClient();
        var manifest = await youtube.Videos.Streams.GetManifestAsync(song.Url);
        var streamInfo = manifest.GetAudioOnlyStreams().FirstOrDefault();
        if (streamInfo == null)
        {
            Console.WriteLine("Аудиопоток не найден.");
            return;
        }

        var audioStream = await youtube.Videos.Streams.GetAsync(streamInfo);
        var audioClient = connection.CreatePCMStream(AudioApplication.Mixed);
        await audioStream.CopyToAsync(audioClient);

        _ = Task.Run(async () =>
        {
            await PlayAsync(guild, _queue[guild.Id].Dequeue(), connection);
        });
    }

    private void Stop(SocketCommandContext context, Queue<SongInfo> serverQueue)
    {
        if (serverQueue.Count == 0)
        {
            context.Channel.SendMessageAsync("Нет песен в очереди!").GetAwaiter().GetResult();
            return;
        }

        serverQueue.Clear();
        (context.Guild.AudioClient as IVoiceChannel).DisconnectAsync().GetAwaiter().GetResult();
        _queue.Remove(context.Guild.Id);
        context.Channel.SendMessageAsync("Музыка остановлена").GetAwaiter().GetResult();
    }

    private void Next(SocketCommandContext context, Queue<SongInfo> serverQueue)
    {
        if (serverQueue.Count == 0)
        {
            context.Channel.SendMessageAsync("В очереди нет песен!").GetAwaiter().GetResult();
            return;
        }

        serverQueue.Dequeue();
        if (serverQueue.Count > 0)
        {
            PlayAsync(context.Guild, serverQueue.Peek(), context.Guild.AudioClient).GetAwaiter().GetResult();
        }
        else
        {
            context.Channel.SendMessageAsync("В очереди нет следующего трека!").GetAwaiter().GetResult();
        }
    }

    private void Clear(SocketCommandContext context, string arg)
    {
        if (!int.TryParse(arg, out var number))
        {
            context.Channel.SendMessageAsync("Вы ввели не число").GetAwaiter().GetResult();
            return;
        }

        if (number > 100)
        {
            context.Channel.SendMessageAsync("Я не могу удалить больше 100 сообщений").GetAwaiter().GetResult();
            return;
        }

        if (number < 1)
        {
            context.Channel.SendMessageAsync("Введите число больше чем 1").GetAwaiter().GetResult();
            return;
        }

        var messages = context.Channel.GetMessagesAsync(number + 1).FlattenAsync().GetAwaiter().GetResult();
        foreach (var msg in messages)
        {
            msg.DeleteAsync().GetAwaiter().GetResult();
        }
    }

    private void Help(SocketCommandContext context)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Меню помощи")
            .WithDescription("Здесь перечислены все доступные команды")
            .WithAuthor("AdmiralMusic", context.Client.CurrentUser.GetAvatarUrl())
            .AddField("Доступные команды:", "play, next, clear, stop")
            .AddField("play", "!play URL : Проигрывает музыку по ссылке, если написать несколько раз, то песня добавится в очередь")
            .AddField("next", "!next : Включает следующую песню из очереди")
            .AddField("clear", "!clear N: Удаляет 'N' сообщений")
            .AddField("stop", "!stop : Выходит из голосового канала, при этом очередь не сохраняется!")
            .WithColor(Color.Green)
            .WithFooter("Приятного пользования!")
            .WithCurrentTimestamp()
            .Build();

        context.Channel.SendMessageAsync(embed: embed).GetAwaiter().GetResult();
    }

    private class SongInfo
    {
        public string Title { get; set; }
        public string Url { get; set; }
    }
}

