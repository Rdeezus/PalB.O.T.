// PalB.O.T. 1.0
// Discord Soundboard Application
// Ryan Brown and Michael Zahner
// 2024

using Discord;
using Discord.WebSocket;

namespace PalBot
{
    public class PalBotApp
    {
        // Discord Bot Admin ID's
        // Can change this to checking what the top role/exact role is and creating one above clutter so other pals can't add the role themselves.
        private static HashSet<ulong> Admins = BuildAdminTable();

        private static HashSet<ulong> BuildAdminTable()
        {
            HashSet<ulong> temp = new HashSet<ulong>();
            temp.Add(66152954847039488);   // Michael Zahner, @zetahhhhhhhhhhhhhhhhhhhhhhhhhhhh
            temp.Add(82980874429140992);   // Ryan Brown, @thoronous
            temp.Add(246046972094578690);  // Sidney ???, @thirtyfour
            return temp;
        }

        // Creating the Discord Client
        private static DiscordSocketClient? PalClient;

        // Initiate soundpack stuff and create queue.
        private static SoundPack Sound = new SoundPack();

        private static CommandList PalBotCmd = new CommandList();

        private static Queue<SocketMessage> msgQueue = new Queue<SocketMessage>();

        //hash set for command prefixes to pass valid messages to the queue
        private static HashSet<Char> prefixTable = BuildPrefixTable();

        //PriorityQueue<SocketMessage, int> audioQueue = new PriorityQueue<SocketMessage, int>();
        public static bool POWERED = true;

        //prefix table used to determine if a message is a command in O(1) by checking if the first character of the message exists in the set
        private static HashSet<Char> BuildPrefixTable()
        {
            HashSet<Char> temp = new HashSet<Char>();
            temp.Add('.');
            temp.Add('!');
            temp.Add('$');
            return temp;
        }

        // Program Initiator
        public static async Task Main(string[] args)
        {
            //declare instance for static variables
            var program = new PalBotApp();

            //sets up Discord gateway intents and attaches logging and the event listener to the client
            await Task.Run(() => program.MainAsync());

            //hold the client open perma, one task to monitor the message queue populated by clientonmessagereceived and one auxiliary service (voice and audio)
            Task.Run(() => program.ProcessQueue(msgQueue, Sound, PalClient, PalBotCmd));
            await Task.Run(() => Sound.ProcessSoundQueue());
            System.Environment.Exit(0);
        }

        // Program Start
        public async Task MainAsync()
        {
            // Set Discord Socket Gateway config for message content. 
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildVoiceStates | GatewayIntents.Guilds
            };

            // Create socket to connect to discord and attach log
            PalClient = new DiscordSocketClient(config);
            PalClient.Log += Log;
            // Sets on message create handler
            PalClient.MessageReceived += ClientOnMessageRecieved;
            // Start the client with loading the token from local /bin/debug/net.8/key.txt
            await PalClient.LoginAsync(TokenType.Bot, File.ReadAllText("key.txt"));
            await PalClient.StartAsync();
        }

        //essentially, most of the logic here was just copy pasted from previous iteration
        //only significant changes made so far were adding the queue, and looping through the process "command" (to be implemented) while powered on
        public async Task ProcessQueue(Queue<SocketMessage> msgQueue, SoundPack Sound, DiscordSocketClient PalClient, CommandList PalBotCmd)
        {
            while (POWERED)
            {
                //again, most of the following logic is placeholder and should be reworked to some kind of hashset or dict
                if (msgQueue.Count > 0) 
                {
                    SocketMessage msg = msgQueue.Dequeue();
                    // If message starts with desired prefix, execute sound command lookup
                    if (msg.Content[0] == '.')
                    {
                        Sound.ParseSoundMsg(PalClient, msg);
                    }
                    // User/admin command check
                    else if (PalBotCmd.Cmd.Contains(msg.Content.Split()[0]))
                    {
                        // basic user command
                        if (msg.Content[0] == '!')
                        {
                            Action<DiscordSocketClient, SocketMessage> newAction = AudioCommandDict[msg.Content.Split()[0]];
                            newAction(PalClient, msg);
                        }
                        // admin command
                        else if (msg.Content[0] == '$' && Admins.Contains((ulong)msg.Author.Id))
                        {
                            Action<DiscordSocketClient, SocketMessage> newAction = AdminCommandDict[msg.Content.Split()[0]];
                            newAction(PalClient, msg);
                        }
                        // no default else because it has to match a command
                        // don't think we need to add a default !help return in case of unknown command use other than wrong syntax
                        // would just be extra clutter in the channels or unintended bot replies
                    }
                    else
                    {
                        // no command or prefix found, skip message.
                    }
                }
                // saving cycles but surely there is a better way to process events, too eepy to fink about it
                Thread.Sleep(50);
            }
            return;
        }

        // Handles Discord.NET's log events.
        private static Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        // Message Handler
        private async Task ClientOnMessageRecieved(SocketMessage msg)
        {
            // Ignores itself and other bot messages
            if (!msg.Author.IsBot && msg.Content != string.Empty && prefixTable.Contains(msg.Content[0]))
            {
                msgQueue.Enqueue(msg);
            }
            return;
        }

        private static readonly Dictionary<String, Action<DiscordSocketClient, SocketMessage>> AudioCommandDict = BuildAudioCommandTable();

        private static Dictionary<String, Action<DiscordSocketClient, SocketMessage>> BuildAudioCommandTable()
        {
            Dictionary<String, Action<DiscordSocketClient, SocketMessage>> temp = new Dictionary<String, Action<DiscordSocketClient, SocketMessage>>();
            Action<DiscordSocketClient, SocketMessage> tempAction;

            tempAction = (d, s) => { Task.Run(() => Sound.VoiceConnect(d, s)); };
            temp.Add("!join", tempAction);

            tempAction = (d, s) => { Task.Run(() => Sound.VoiceDC(s)); };
            temp.Add("!leave", tempAction);

            tempAction = (d, s) => { Task.Run(() => Sound.AddSound(s)); };
            temp.Add("!add", tempAction);

            tempAction = (d, s) => { Task.Run(() => Sound.DeleteSound(s)); };
            temp.Add("!delete", tempAction);

            tempAction = (d, s) => { Task.Run(() => Sound.EditSound(s)); };
            temp.Add("!edit", tempAction);

            tempAction = (d, s) => { Task.Run(() => Sound.StopAudio()); };
            temp.Add("!stop", tempAction);

            tempAction = (d, s) => { Task.Run(() => SoundPack.CheckSoundQueue(s)); };
            temp.Add("!check", tempAction);

            tempAction = (d, s) => { Task.Run(() => Sound.ClearSoundQueue(s)); };
            temp.Add("!clear", tempAction);

            tempAction = (d, s) => { Task.Run(() => PalBotCmd.GetCommandList(Sound, s)); };
            temp.Add("!commands", tempAction);

            tempAction = (d, s) => { Task.Run(() => SoundPack.GetHelp(s)); };
            temp.Add("!help", tempAction);

            return temp;
        }

        private static readonly Dictionary<String, Action<DiscordSocketClient, SocketMessage>> AdminCommandDict = BuildAdminCommandTable();

        private static Dictionary<String, Action<DiscordSocketClient, SocketMessage>> BuildAdminCommandTable()
        {
            Dictionary<String, Action<DiscordSocketClient, SocketMessage>> temp = new Dictionary<String, Action<DiscordSocketClient, SocketMessage>>();
            Action<DiscordSocketClient, SocketMessage> tempAction;

            tempAction = (d, s) => { Task.Run(async () => { 
                if (Sound.IsConnected())
                {
                    await Task.Run(() => Sound.VoiceDC(s));
                }
                await s.Channel.SendMessageAsync("gn forever");
                await Task.Run(() => PalClient.LogoutAsync());
                POWERED = false;
                });
            };
            temp.Add("$sd", tempAction);

            tempAction = (d, s) => {
                Task.Run(async () => {
                    Sound.RaidMode = !Sound.RaidMode;
                    if (Sound.RaidMode)
                    {
                        await s.Channel.SendMessageAsync("Raid mode on. Sound commands are limited to " + Sound.getMax().ToString() + "s.");
                    }
                    else
                    {
                        await s.Channel.SendMessageAsync("Raid mode off. Sound commands are fully enabled.");
                    }
                });
            };
            temp.Add("$raid", tempAction);

            tempAction = (d, s) => { Task.Run(() => AdminWhitelistAdd(s)); };
            temp.Add("$whitelist", tempAction);

            tempAction = (d, s) => { Task.Run(() => AdminWhitelistRemove(s)); };
            temp.Add("$dewhitelist", tempAction);

            return temp;
        }

        private static void AdminWhitelistAdd(SocketMessage msg)
        {
            // adds mentioned user ID to admin whitelist, currently not check for any erroneous input so tell the users to not fuck it up thanks
            Admins.Add(MentionUtils.ParseUser(msg.Content.Split()[1]));
            msg.Channel.SendMessageAsync("New admin " + MentionUtils.ParseUser(msg.Content.Split()[1]) + " has been whitelisted");
        }

        private static void AdminWhitelistRemove(SocketMessage msg)
        {
            // removes mentioned user ID from admin whitelist, currently not check for any erroneous input so tell the users to not fuck it up thanks
            Admins.Remove(MentionUtils.ParseUser(msg.Content.Split()[1]));
            msg.Channel.SendMessageAsync(MentionUtils.ParseUser(msg.Content.Split()[1]) + " has been removed from the admin whitelist");
        }
    }
}