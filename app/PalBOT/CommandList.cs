using Discord.WebSocket;

namespace PalBot
{
    public class CommandList
    {
        /*
        private struct CommandPriority
        {
            string cmd;
            int prio;
            
            private CommandPriority(string cmd, int prio)
            {
                this.cmd = cmd;
                this.prio = prio;
            }
         */

        // Availble cmd's
        public string[] Cmd = new string[]
            {
            // admin only commands
            "$sd",      // shutdown, remotely if necessary
            "$raid",    // Changes bots configuration for allowed sound commands.
            "$log",     // Uploads the current logfile. maybe restrict it to dm of the admin invoker?
            "$whitelist",   // Whitelist user for admin commands (maybe check superadmin status?)
            "$dewhitelist", // Remove user from whitelist for admin commands (maybe check superadmin status?)
            "$blacklist",   // Add user to blacklist to prevent them from interacting with the bot (maybe check superadmin status?)
            "$deblacklist", // User is reformed, save them from purgatory (maybe check superadmin status?)
            // user allowed commands
            "!add",     // Add a sound to the bot
            "!delete",  // Delete a sound from the bot
            "!edit",    // Edit the volume, command name, or audio file of the bot.
            "!stop",    // Skips 1 command sound. Maybe can make it skip x amount, eh.
            "!clear",   // Empties the audio command queue
            "!check",   // Checks the length of the audio queue
            "!commands",// Uploads a text file of all the commands in the bot
            "!join",    // Makes bot come to invokers vc
            "!leave",   // Makes bot disconnect from vc
            "!8ball",   // Generates random yes/no/maybe response
            "!help"     // Generates a help file for commands and syntax
            };

            // TO DO
            // Add functionality to all those commands here
            // Not certain on functionality or function callbacks grow from here or still from within function section
            // IE should add, delete, edit be implemented elsewhere and not defined here other than being in the command list

        public void GetCommandList(SoundPack sp, SocketMessage msg)
        {
            var sm = sp.GetMap();
            string[] cl = new string[sm.Count];
            StreamWriter cmdFile = new StreamWriter("commandlist.txt");
            int i = 0;
            foreach (var cmd in sm)
            {
                cl[i] = cmd.Key;
                i++;
            }
            Array.Sort(cl);
            for (i = 0; i < sm.Count; i++)
            {
                cmdFile.WriteLine(cl[i]);
            }
            cmdFile.Close();
            msg.Channel.SendFileAsync("commandlist.txt");
        }
    }
}
