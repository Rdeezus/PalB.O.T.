using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System.Diagnostics;
using System.Net;
using System.Timers;
using Xabe.FFmpeg;

namespace PalBot
{
    public class SoundPack
    {
        // Hash map for commands : properties
        public Dictionary<string, SoundProp> SoundMap = new Dictionary<string, SoundProp>();
        IAudioClient AudioClient;
        AudioOutStream VCAudioStream;
        public static bool AudioPlaying = false;
        public bool RaidMode = false;
        private static float AudioDurationMax = 5f;
        private static System.Timers.Timer AudioTimer = new System.Timers.Timer();  //audio timer to prevent lockout on ffmpeg?
        public static Queue<QueueProp> SoundQueue = new Queue<QueueProp>();  //queue for handling audio queues
        private static bool POWERED = true;
        private static CancellationTokenSource TimerSource = new CancellationTokenSource();
        private static CancellationTokenSource AudioTokenSource = new CancellationTokenSource();
        private static CancellationToken AudioToken = AudioTokenSource.Token;
        private static bool stopAudioVar = false;
        private static bool audioIsStreaming = false;

        // Struct for sound properties to store in hash map
        public struct SoundProp
        {
            // Properties of the command and audio.
            string FilePath;
            float Volume;
            float Duration;

            // Default struct constructor
            public SoundProp(string fp, float vol, float dur)
            {
                this.FilePath = fp;
                this.Volume = vol;
                this.Duration = dur;
            }

            public string getFP()
            { 
                return this.FilePath; 
            }

            public double getVol()
            {
                return this.Volume; 
            }
        
            public double getDur()
            { 
                return this.Duration; 
            }
        }

        public struct QueueProp
        {
            SoundProp cmdProp;
            DiscordSocketClient client;
            SocketMessage msg;

            public QueueProp(SoundProp sp, DiscordSocketClient client, SocketMessage msg)
            {
                this.cmdProp = sp;
                this.client = client;
                this.msg = msg;
            }

            public SocketMessage getMsg()
            {
                return this.msg;
            }
            public DiscordSocketClient getClient()
            {
                return this.client;
            }
            public SoundProp getProp()
            { 
                return this.cmdProp; 
            }
        }

        // Default constructor, used to initiate sound pack stuff.
        public SoundPack()
        {
            // Holders for lines and splitting the csv
            String line;
            string[] data;

            // Open csv file
            StreamReader sr = new StreamReader("sound.csv");
            line = sr.ReadLine();

            // Loop until end of file
            while (line != null)
            {
                // Split the csv file lines by ,
                data = line.Split(',');

                // Create the struct to hold the data
                // Add it to the map with the key:value pair of Command : Properties
                this.SoundMap.Add(data[0], new SoundProp(data[1], float.Parse(data[2]), float.Parse(data[3])));

                // Read next line
                line = sr.ReadLine();
            }
            sr.Close();

            // set a timer object for audio playing
            //AudioTimer.Elapsed += OnTimedEvent;
        }

        public async Task ParseSoundMsg(DiscordSocketClient client, SocketMessage msg)
        {
            string[] MsgCommands = msg.Content.Split();

            foreach (string cmd in MsgCommands)
            {
                var bot = await msg.Channel.GetUserAsync(client.CurrentUser.Id);
                IVoiceChannel uChannel = null;
                uChannel = uChannel ?? ((msg.Author as IGuildUser)?.VoiceChannel);



                // check if bot is in vc, then check if user is in vc, then check if in same vc
                if (this.IsConnected() && uChannel != null && bot is IGuildUser botUser && uChannel.Id == botUser.VoiceChannel.Id)
                {
                    this.ParseVoiceCmd(client, msg, MsgCommands, cmd);
                }
                else
                {
                    if (this.SoundMap.ContainsKey(cmd)){
                        this.UploadAudio(this.GetSound(cmd), msg);
                    }
                    else
                    {
                        msg.Channel.SendMessageAsync(cmd + " command not found.");
                    }
                }
            }
        }

        private void ParseVoiceCmd(DiscordSocketClient client, SocketMessage msg, string[] MsgCommands, string cmd)
        {
            int randomNum = 0;
            if (int.TryParse(cmd, out randomNum))
            {
                // if it's a number intended for random, do nothing.
                return;
            }
            else if (cmd != ".random" && this.RaidMode && this.SoundMap.TryGetValue(cmd, out SoundProp temp) && this.SoundMap[cmd].getDur() > AudioDurationMax)
            {
                //raid mode on, is a command, and it's longer than set duration. skip command.
                msg.Channel.SendMessageAsync(cmd + " is too long for raid mode.");
                return;
            }
            else if (SoundMap.TryGetValue(cmd, out SoundProp value))
            {
                //add normal command to the queue.
                SoundQueue.Enqueue(new QueueProp(this.GetSound(cmd), client, msg));
            }
            else if (cmd == ".random")
            {
                try
                {
                    int randomCount = 0;
                    if (MsgCommands.Length > Array.IndexOf(MsgCommands, cmd) + 1 && int.TryParse(MsgCommands[Array.IndexOf(MsgCommands, cmd) + 1], out randomCount))
                    {
                        // checks if there's a number to check in the msg following random
                        // loops through to queue x audio
                        if (randomCount > 9 || randomCount < 1)
                        {
                            msg.Channel.SendMessageAsync("Please limit random to a maximum of 9.");
                        }
                        else
                        {
                            for (int i = 0; i < randomCount; i++)
                            {   // put random x in queue
                                SoundQueue.Enqueue(new QueueProp(this.PlayRandom(), client, msg));
                            }
                        }
                    }
                    else
                    {   // random is only called once.
                        SoundQueue.Enqueue(new QueueProp(this.PlayRandom(), client, msg));
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            }
            else
            {
                msg.Channel.SendMessageAsync(cmd + " command not found.");
            }
        }

        public async Task ProcessSoundQueue()
        {
            QueueProp DQProp;
            while (PalBotApp.POWERED)
            {
                while(AudioPlaying)
                {
                    //sit idle.
                }
                if (SoundQueue.Count > 0 && AudioClient.ConnectionState == ConnectionState.Connected)
                {
                    AudioPlaying = true;
                    DQProp = SoundQueue.Dequeue();
                    //await SendAudio(this.AudioClient, DQProp.getProp());
                    await Task.Run(() => this.PlaySound(DQProp));
                    AudioPlaying = false;
                }
                Thread.Sleep(50);
            }
            return;
        }

        // add a custom sound
        public async Task AddSound(SocketMessage msg)
        {
            string[] msgAdd = msg.Content.Split();
            try 
            {
                // check if at least !add and .command is invoked
                if(msgAdd.Length < 2)
                {
                    // command name is missing
                    await msg.Channel.SendMessageAsync("Syntax error on adding command. Please use !add .[command] [volume 1-100]");
                    throw new Exception("Syntax error on adding command. Please attach audio file and use !add .[command] [volume 1-100]");
                }
                else if (SoundMap.ContainsKey(msgAdd[1]))
                {
                    // command already exists in the map
                    await msg.Channel.SendMessageAsync("Command already exists.");
                    throw new Exception("Command already exists.");
                }
                // check for right file type
                // currently allows mp3, ogg, wav, and flac. I think ffmpeg supports flac natively.
                if (new string[] { "audio/mpeg", "audio/x-wav", "audio/ogg", "audio/flac" }.Contains(msg.Attachments.ElementAt(0).ContentType))
                {
                    // check if valid command
                    if (!msgAdd[1].StartsWith('.'))
                    {
                        throw new Exception("Command must start with a .");
                    }

                    //it's an audio file so dl it
                    float vol;
                    if (msgAdd.Length < 3)
                    {
                        // no volume provided, defaulted to 50% to be safe
                        vol = 0.5f;
                    }
                    else
                    {
                        // convert volume to decimal amount.
                        vol = float.Parse(msgAdd[2]) / 100f;
                    }

                    // volume range too big or small/negative.
                    if (vol > 1f || vol <= 0f)
                    {
                        throw new Exception("Please set volume between 1-100.");
                    }
                    Console.WriteLine("gm bitch part 1");
                    // download the file and get the duration of the audio file.
                    var download = new WebClient(); // need in order to download from url
                    Console.WriteLine("gm bitch part 2");
                    // set filepath and trimming discord's garbage url generator for naming.
                    string AudioFP = "Soundpack/Custom/" + msg.Attachments.ElementAt(0).Filename.Split('.')[0] + msg.Id + Path.GetExtension(msg.Attachments.ElementAt(0).Url.Split('?')[0]);
                    Console.WriteLine("gm bitch part 3");
                    download.DownloadFile(new Uri(msg.Attachments.ElementAt(0).Url), AudioFP);
                    Console.WriteLine("gm bitch part 4");
                    TimeSpan dur = FFmpeg.GetMediaInfo(AudioFP).Result.Duration;
                    Console.WriteLine("gm bitch part 5");

                    // add to the map, update command list and file.
                    this.SoundMap.Add(msgAdd[1], new SoundProp(AudioFP, vol, (float)dur.TotalSeconds));
                    Console.WriteLine("gm bitch part 6");
                    await this.UpdateCommands();
                    Console.WriteLine("gm bitch part 7");
                    await msg.Channel.SendMessageAsync("Sound command added.");
                    Console.WriteLine("gm bitch part 8");
                    download.Dispose();
                    Console.WriteLine("gm bitch part 9");
                    return;
                }
                else
                {
                    throw new Exception("Please attach an audio file type (currently .mp3 .ogg .flac. and .wav)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return;
            }
        }

        // delete a sound
        public async Task DeleteSound(SocketMessage msg)
        {
            string[] msgDel = msg.Content.Split();
            try
            {
                if (this.SoundMap.ContainsKey(msgDel[1]))
                {
                    this.DeleteCommand(msgDel[1]);
                    await this.UpdateCommands();
                    await msg.Channel.SendMessageAsync(msgDel[1] + " command and file deleted.");
                    return;
                }
                else
                {
                    throw new Exception("Wrong Syntax: Deleting command.");
                }
            }
            catch (Exception ex)
            {
                await msg.Channel.SendMessageAsync("Command not found to be deleted.");
                return;
            }
        }

        private void DeleteCommand(string msgDel)
        {
            File.Delete(this.SoundMap[msgDel].getFP());
            this.SoundMap.Remove(msgDel);
        }

        //edit existing sound command name, volume, and file.
        public async Task EditSound(SocketMessage msg)
        {
            string[] msgEdit = msg.Content.Split();
            if(msgEdit.Length < 2)
            {
                await msg.Channel.SendMessageAsync("Syntax error: editing command; must use !edit .[command] [.newcommandname] [volume 1-100] with optional replacement audio file.");
            }
            if (this.SoundMap.ContainsKey(msgEdit[1]))
            {
                SoundProp SoundEdit = this.SoundMap[msgEdit[1]];    // grab existing properties to be reused for missing properties.
                // editing audio file
                if(msg.Attachments.Count == 1)
                {
                    if (msgEdit.Length == 2)
                    {
                        // changing only the audio file, default vol.
                        SoundEdit = await EditProp(msg, SoundEdit, optionalFile: msg.Attachments.Count);
                        SoundProp tempEdit;
                        if (SoundEdit.Equals(this.SoundMap.TryGetValue(msgEdit[1], out tempEdit)))
                        {
                            // if the file is unchanged from the original, error in downloading attachment or wrong type.
                            return;
                        }
                        this.DeleteCommand(msgEdit[1]);             // remove old "command"
                        this.SoundMap.Add(msgEdit[1], SoundEdit);   // add the new version with the original command name
                        await this.UpdateCommands();                // rewrite the csv value
                        await msg.Channel.SendMessageAsync(msgEdit[1] + " edited.");
                        return;
                    }
                    else if(msgEdit.Length == 3)
                    {
                        // changing audio and volume or command name
                        float newVol;
                        if (float.TryParse(msgEdit[2], out newVol))
                        {
                            // 3rd argument is a number so volume is being changed
                            if (newVol < 1f || newVol > 100f)
                            {
                                await msg.Channel.SendMessageAsync("Syntax error : Volume must be between 1-100");
                                return;
                            }
                            SoundEdit = await EditProp(msg, SoundEdit, optionalFile: msg.Attachments.Count, optionalVol: newVol);
                            SoundProp tempEdit;
                            if (SoundEdit.Equals(this.SoundMap.TryGetValue(msgEdit[1], out tempEdit)))
                            {
                                // if the file is unchanged from the original, error in downloading attachment or wrong type.
                                return;
                            }
                            this.DeleteCommand(msgEdit[1]);             // remove old command
                            this.SoundMap.Add(msgEdit[1], SoundEdit);   // add the new version with old command name.
                            await this.UpdateCommands();                // update csv file
                            await msg.Channel.SendMessageAsync(msgEdit[1] + " edited.");
                            return;
                        }
                        else
                        {
                            // 3rd argument isn't a number so command name is being changed with audio
                            // check if new command name already exists
                            if (SoundMap.ContainsKey(msgEdit[2]))
                            {
                                await msg.Channel.SendMessageAsync("New command name already exists.");
                                return;
                            }
                            else if (!msgEdit[2].StartsWith('.'))
                            {
                                await msg.Channel.SendMessageAsync("New command name must include . at the start of the command.");
                            }
                            else
                            {
                                SoundEdit = await EditProp(msg, SoundEdit, optionalFile: msg.Attachments.Count);
                                SoundProp tempEdit;
                                if (SoundEdit.Equals(this.SoundMap.TryGetValue(msgEdit[1], out tempEdit)))
                                {
                                    // if the file is unchanged from the original, error in downloading attachment or wrong type.
                                    return;
                                }
                                this.DeleteCommand(msgEdit[1]);             // remove old command
                                this.SoundMap.Add(msgEdit[2], SoundEdit);   // add the new command name
                                await this.UpdateCommands();                // update csv file
                                await msg.Channel.SendMessageAsync(msgEdit[1] + " edited.");
                                return;
                            }
                        }
                    }
                    else if (msgEdit.Length == 4)
                    {
                        // changing audio, volume, and command.
                        float newVol;
                        if (SoundMap.ContainsKey(msgEdit[2]))
                        {
                            await msg.Channel.SendMessageAsync("New command name already exists.");
                            return;
                        }
                        else if (!float.TryParse(msgEdit[3], out newVol))
                        {
                            await msg.Channel.SendMessageAsync("Syntax error: editing command; must use !edit .[command] [.newcommandname] [volume 1-100] with optional replacement audio file attached.");
                            return; // last argument isn't a number, improper command usage.
                        }
                        else if (newVol < 1f || newVol > 100f)
                        {
                            await msg.Channel.SendMessageAsync("Syntax error : Volume must be between 1-100");
                            return;
                        }
                        else if (!msgEdit[2].StartsWith('.'))
                        {
                            await msg.Channel.SendMessageAsync("New command name must include . at the start of the command.");
                        }
                        else
                        {
                            SoundEdit = await EditProp(msg, SoundEdit, optionalFile: msg.Attachments.Count, optionalVol: newVol);
                            SoundProp tempEdit;
                            if (SoundEdit.Equals(this.SoundMap.TryGetValue(msgEdit[1], out tempEdit)))
                            {
                                // if the file is unchanged from the original, error in downloading attachment or wrong type.
                                return;
                            }
                            this.DeleteCommand(msgEdit[1]);             // remove old command
                            this.SoundMap.Add(msgEdit[2], SoundEdit);   // add the new version with old command name.
                            await this.UpdateCommands();                // update csv file
                            await msg.Channel.SendMessageAsync(msgEdit[1] + " edited.");
                            return;
                        }
                    }
                    else
                    {
                        await msg.Channel.SendMessageAsync("Syntax error: editing command.");
                        return;
                    }
                }
                // changing command and/or volume. audio stays the same.
                else if (msg.Attachments.Count == 0)
                {
                    if(msgEdit.Length < 3)
                    {
                        // nothing is being changed
                        await msg.Channel.SendMessageAsync("Syntax error: editing command.");
                    }
                    else if(msgEdit.Length == 3)
                    {
                        // command name or volume changing
                        float newVol;
                        if (float.TryParse(msgEdit[2], out newVol))
                        {
                            // 3rd argument is a number so volume is being changed
                            if (newVol < 1f || newVol > 100f)
                            {
                                await msg.Channel.SendMessageAsync("Syntax error : Volume must be between 1-100");
                                return;
                            }
                            SoundEdit = await EditProp(msg, SoundEdit, optionalVol: newVol);    // edit a new soundprop object with updates
                            this.SoundMap[msgEdit[1]] = SoundEdit;                              // replace the old properties with the new one.
                            await this.UpdateCommands();                                        // update csv file
                            await msg.Channel.SendMessageAsync(msgEdit[1] + " edited.");
                            return;
                        }
                        else
                        {
                            // 3rd argument isn't a number so only command name is being changed
                            // check if new command name already exists
                            if (SoundMap.ContainsKey(msgEdit[2]))
                            {
                                await msg.Channel.SendMessageAsync("New command name already exists.");
                                return;
                            }
                            else if (!msgEdit[2].StartsWith('.'))
                            {
                                await msg.Channel.SendMessageAsync("New command name must include . at the start of the command.");
                            }
                            else
                            {
                                this.SoundMap.Remove(msgEdit[1]);           // deleting old command reference
                                this.SoundMap.Add(msgEdit[2], SoundEdit);   // add the new command name with old properties.
                                await this.UpdateCommands();                // update csv file
                                await msg.Channel.SendMessageAsync(msgEdit[1] + " edited.");
                                return;
                            }
                        }
                    }
                    else if (msgEdit.Length == 4)
                    {
                        // command and volume is being changed
                        float newVol;
                        if (SoundMap.ContainsKey(msgEdit[2]))
                        {
                            await msg.Channel.SendMessageAsync("New command name already exists.");
                            return;
                        }
                        else if (!float.TryParse(msgEdit[3], out newVol))
                        {
                            await msg.Channel.SendMessageAsync("Syntax error: editing command; must use !edit .[command] [.newcommandname] [volume 1-100] with optional replacement audio file attached.");
                            return; // last argument isn't a number, improper command usage.
                        }
                        else if (newVol < 1f || newVol > 100f)
                        {
                            await msg.Channel.SendMessageAsync("Syntax error : Volume must be between 1-100");
                            return;
                        }
                        else if (!msgEdit[2].StartsWith('.'))
                        {
                            await msg.Channel.SendMessageAsync("New command name must include . at the start of the command.");
                        }
                        else
                        {
                            SoundEdit = await EditProp(msg, SoundEdit, optionalVol: newVol);
                            this.SoundMap.Remove(msgEdit[1]);           // deleting old command reference
                            this.SoundMap.Add(msgEdit[2], SoundEdit);   // add the new version with new command name.
                            await this.UpdateCommands();                // update csv file
                            await msg.Channel.SendMessageAsync(msgEdit[1] + " edited.");
                            return;
                        }
                    }
                }
                else
                {
                    await msg.Channel.SendMessageAsync("Syntax error: editing command.");
                    return;
                }
            }
            else
            {
                await msg.Channel.SendMessageAsync("Command not found to edit.");
                return;
            }
        }

        private static async Task<SoundProp> EditProp(SocketMessage msg, SoundProp PropEdit, double optionalVol = 50f, int optionalFile = 0)
        {
            try
            {
                // file attachment count > 0, edit it and check for type.
                if (optionalFile > 0 && new string[] { "audio/mpeg", "audio/x-wav", "audio/ogg", "audio/flac" }.Contains(msg.Attachments.ElementAt(0).ContentType))
                {
                    var download = new WebClient(); // need in order to download from url
                                                    // new filepath
                    string AudioFP = "Soundpack/Custom/" + msg.Attachments.ElementAt(0).Filename.Split('.')[0] + msg.Id + Path.GetExtension(msg.Attachments.ElementAt(0).Url.Split('?')[0]);
                    //dl the file
                    download.DownloadFile(new Uri(msg.Attachments.ElementAt(0).Url), AudioFP);
                    double dur = FFmpeg.GetMediaInfo(AudioFP).Result.Duration.TotalSeconds;
                    optionalVol = optionalVol / 100f;
                    // return new soundprop item
                    return new SoundProp(AudioFP, (float)optionalVol, (float)dur);
                }
                else
                {
                    // only volume being changed, return new object
                    optionalVol = optionalVol / 100f;
                    return new SoundProp(PropEdit.getFP(), (float)optionalVol, (float)PropEdit.getDur());
                }
            }
            catch (Exception ex){
                await msg.Channel.SendMessageAsync("Wrong file type.");
                return PropEdit;
            }
        }

        //save the command file
        private async Task UpdateCommands()
        {
            var writer = new StreamWriter("sound.csv");
            foreach(var cmd in this.SoundMap)
            {
                writer.WriteLine("{0},{1},{2},{3}", cmd.Key, this.SoundMap[cmd.Key].getFP(), this.SoundMap[cmd.Key].getVol(), this.SoundMap[cmd.Key].getDur());
            }
            writer.Close();
        }

        // FFMpeg process loading
        private Process CreateStream(SoundProp path)
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-hide_banner -loglevel panic -i \"{path.getFP()}\" -af \"volume={path.getVol()}\"  -ac 2 -f s16le -ar 48000 pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
            });
        }
        
        private async Task SendAudio(IAudioClient client, SoundProp path)
        {
            // Create FFmpeg using the previous example
            using (var ffmpeg = CreateStream(path))
            using (var output = ffmpeg.StandardOutput.BaseStream)
            {
                //task to play audio followed by loop to check if playing
                try
                {
                    audioIsStreaming = true;
                    Task.Run(() => AudioStreamSetup(output));
                    await Task.Run(() => MonitorAudioStream(output, ffmpeg));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                //flushing the stream
                finally
                {
                    audioIsStreaming = false;
                    stopAudioVar = false;
                }
                return;
            }
        }

        private async Task AudioStreamSetup(Stream? stream)
        {
            await stream.CopyToAsync(VCAudioStream);
            audioIsStreaming = false;
        }

        private static async Task MonitorAudioStream(Stream? stream, Process proc)
        {
            while(!stopAudioVar && audioIsStreaming)
            {

            }
            stream?.Flush();
            proc.Dispose();
            stopAudioVar = false;
        }

        private static void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            AudioTimer.Enabled = false;
            TimerSource.Cancel();
            //AudioPlaying = false;
        }

        // Play Audio
        public async Task PlaySound(QueueProp qp)
        {
            CancellationToken token = TimerSource.Token;
            AudioTimer = new System.Timers.Timer(qp.getProp().getDur() * 1000 + 50); //convert seconds to ms
            AudioTimer.Enabled = true;
            await Task.Run(() => SendAudio(AudioClient, qp.getProp()), token);
        }

        public SoundProp PlayRandom()
        {
            Random rand = new Random();
            SoundProp tempSound = SoundMap.ElementAt(rand.Next(0, SoundMap.Count)).Value;
            while(this.RaidMode && tempSound.getDur() > AudioDurationMax)
            {
                tempSound = SoundMap.ElementAt(rand.Next(0, SoundMap.Count)).Value;
            }
            return tempSound;
        }

        private async Task UploadAudio(SoundProp sp, SocketMessage msg)
        {
            await msg.Channel.SendFileAsync(sp.getFP());
        }

        public async Task VoiceDC(SocketMessage msg)
        {
            if (IsConnected())
            {
                SoundQueue.Clear();
                this.VCAudioStream.FlushAsync();
                this.VCAudioStream.Close();
                this.AudioClient.StopAsync();
                this.VCAudioStream = null;
                this.AudioClient = null;
                AudioPlaying = false;
            }
            else
            {
                await msg.Channel.SendMessageAsync("I'm currently not in voice.");
            }
        }

        public async Task VoiceConnect(DiscordSocketClient client, SocketMessage msg)
        {
            var bot = await msg.Channel.GetUserAsync(client.CurrentUser.Id);
            IVoiceChannel uChannel = null;
            uChannel = uChannel ?? ((msg.Author as IGuildUser)?.VoiceChannel);

            // checking if invoker's in a vc channel.
            if (uChannel == null)
            {
                // user not in voice
                msg.Channel.SendMessageAsync("You're not in a Voice Chat.");
            }
            else if(IsConnected())
            {
                // user and bot are in same voice already
                if(bot is IGuildUser botUser && uChannel.Id == botUser.VoiceChannel.Id)
                {
                    msg.Channel.SendMessageAsync("I'm already in your VC.");

                }
                else
                {
                    // differing voice chats, stop previous connection and create a new one
                    await this.VoiceDC(msg);
                    this.AudioClient = await (msg.Author as IGuildUser).VoiceChannel.ConnectAsync();
                    VCAudioStream = this.AudioClient.CreatePCMStream(AudioApplication.Mixed);
                }  
            }
            else
            {
                // user is in voice, and bot is not currently connected
                this.AudioClient = await (msg.Author as IGuildUser).VoiceChannel.ConnectAsync();
                VCAudioStream = this.AudioClient.CreatePCMStream(AudioApplication.Mixed);
            }
        }

        public bool IsConnected()
        {
            if (this.AudioClient != null && this.AudioClient.ConnectionState == ConnectionState.Connected)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        // Command getter, not used atm.
        public SoundProp GetSound(string command)
        {
            return this.SoundMap.GetValueOrDefault(command);
        }

        public float getMax()
        {
            return AudioDurationMax;
        }

        public Dictionary<string, SoundProp> GetMap()
        {
            return this.SoundMap;
        }

        //public wrapper to cancel audio currently being streamed to a discord voice channel by flipping a variable monitored in the MonitorAudioStream function
        public async Task StopAudio()
        {
            //instead of checking if AudioPlaying is true, just assign it to stopAudioVar and it should only stop audio if audio is actually playing.
            stopAudioVar = AudioPlaying;
            await this.VCAudioStream.FlushAsync();
            Console.WriteLine("Audio stopped");
        }

        public async Task ClearSoundQueue(SocketMessage msg)
        {
            SoundQueue.Clear();
            await this.StopAudio();
            msg.Channel.SendMessageAsync("Sound queue cleared");
        }

        public static async Task CheckSoundQueue(SocketMessage msg)
        {
            await Task.Run(() => msg.Channel.SendMessageAsync(SoundQueue.Count + " items in queue"));
        }

        public static void GetHelp(SocketMessage msg)
        {
            msg.Channel.SendMessageAsync("PalB.O.T. Audio Module Commands:\n"
                + "\n" + "!join - Connects the bot to your current voice channel\n"
                + "!leave - Disconnects the bot from it's voice channel\n"
                + "!add - Create new audio command using uploaded file, syntax is '!add .commandname {X}' where .commandname can be replaced with desired name, must start with ., and {x} is an optional volume parameter\n"
                + "!delete - Deletes the specified command, syntax is '!delete .commandname'\n"
                + "!edit - Can edit audio commands, volume, and associated files. Good luck on figuring out the syntax because it is too difficult to make the logic succint at the time of writing (meaning I don't want to do it thank you)\n"
                + "!stop - Stops the currently playing audio\n"
                + "!check - Checks how many audio commands are in queue to play\n"
                + "!clear - Stops currently playing audio and clears the audio queue\n"
                + "!commands - Displays a list of available audio file commands\n"
                + "!help - ... how did you get here without !help?\n");

        }
    }
}