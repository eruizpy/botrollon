#region License (GPL v2)
/*
    ChatBot - Proof of concept ChatGPT bot for Rust
    Copyright (c) 2023 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License v2.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License Information (GPL v2)
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;
using System.Diagnostics;
using ConVar;
using Newtonsoft.Json;
using Oxide.Core.Libraries;
using System.Net.Http;
using System.Text;
// Reference: System.Net.Http

namespace Oxide.Plugins
{
    [Info("ChatBot", "RFC1920", "1.0.6")]
    [Description("Uses ChatGPT to get short answers to basic questions.")]
    internal class ChatBot : RustPlugin
    {
        // You must disable Oxide's sandbox for this!
        // See https://umod.org/guides/oxide/disabling-plugin-sandboxing
        private ConfigData configData;
        public static ChatBot Instance;

        private Dictionary<ulong, string> playermessages = new Dictionary<ulong, string>();
        private const string permUse = "chatbot.use";

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        private void LMessage(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));
        #endregion

        private object OnPlayerChat(BasePlayer player, string message, Chat.ChatChannel channel)
        {
            if (string.IsNullOrEmpty(configData.Options.apiKey)) return null;
            if (configData.Options.requirePermission && !permission.UserHasPermission(player.UserIDString, permUse)) return null;

            if (message.StartsWith(configData.Options.keyWord))
            {
                if (!playermessages.ContainsKey(player.userID))
                {
                    playermessages.Add(player.userID, null);
                }

                string newmessage = message.Substring(configData.Options.keyWord.Length).Trim();
                if (string.IsNullOrEmpty(newmessage))
                {
                    Player.Reply(
                        player,
                        Lang("emptyquestion"),
                        ulong.Parse(configData.Options.ChatIcon)
                    );
                    return null;
                }
                GetAIResponse(player.userID, newmessage, channel);
                playermessages[player.userID] = null;
            }
            return null;
        }

        public async void GetAIResponse(ulong userid, string message, Chat.ChatChannel channel)
        {
            CompletionRequest completionRequest = new CompletionRequest
            {
                Model = configData.Options.model,
                Prompt = message,
                MaxTokens = 250
            };
            CompletionResponse completionResponse = new CompletionResponse();

            using (HttpClient httpClient = new HttpClient())
            using (HttpRequestMessage httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/completions"))
            {
                httpReq.Headers.Add("Authorization", $"Bearer {configData.Options.apiKey}");
                string requestString = JsonConvert.SerializeObject(completionRequest);
                httpReq.Content = new StringContent(requestString, Encoding.UTF8, "application/json");
                using (HttpResponseMessage httpResponse = await httpClient.SendAsync(httpReq))
                {
                    if (httpResponse?.IsSuccessStatusCode == true)
                    {
                        string responseString = await httpResponse.Content.ReadAsStringAsync();
                        {
                            if (!string.IsNullOrWhiteSpace(responseString))
                            {
                                completionResponse = JsonConvert.DeserializeObject<CompletionResponse>(responseString);
                            }
                        }
                    }

                    if (completionResponse != null)
                    {
                        string completionText = completionResponse.Choices?[0]?.Text.Trim();
                        if (configData.debug) Puts(completionText);

                        if (string.IsNullOrEmpty(completionText))
                        {
                            playermessages[userid] = Lang("noresponse");
                            Player.Reply(BasePlayer.Find(userid.ToString()), playermessages[userid]);
                            return;
                        }

                        playermessages[userid] = completionText;
                        if (channel == Chat.ChatChannel.Global)
                        {
                            if (configData.debug) Puts("Sending global reply.");
                            foreach (BasePlayer pl in BasePlayer.activePlayerList)
                            {
                                Player.Reply(
                                    pl,
                                    playermessages[userid],
                                    ulong.Parse(configData.Options.ChatIcon)
                                );
                            }
                        }
                        else if (channel == Chat.ChatChannel.Team)
                        {
                            if (configData.debug) Puts("Sending team reply.");
                            RelationshipManager.PlayerTeam team = BasePlayer.Find(userid.ToString()).Team;
                            foreach (ulong pl in team.members)
                            {
                                Player.Reply(
                                    BasePlayer.Find(pl.ToString()),
                                    playermessages[userid],
                                    ulong.Parse(configData.Options.ChatIcon)
                                );
                            }
                        }
                        return;
                    }

                    if (configData.debug) Puts("Request error");
                }
            }
        }

        public void GetAIResponseOxide(ulong userid, string message)
        {
            // Fails.
            CompletionRequest completionRequest = new CompletionRequest
            {
                Model = configData.Options.model,
                Prompt = message,
                MaxTokens = 120
            };
            string requestString = JsonConvert.SerializeObject(completionRequest);
            Dictionary<string, string> headers = new Dictionary<string, string>()
            {
                { "Authorization", $"Bearer {configData.Options.apiKey}" }
            };
            if (configData.debug) Puts($"Sending request: {requestString}");
            webrequest.Enqueue(
                "https://api.openai.com/v1/completions",
                requestString,
                (code, response) =>
                {
                    if (code != 200 || response == null)
                    {
                        if (configData.debug) Puts($"Couldn't get an answer from OpenAI!: {code}");
                        return;
                    }
                    CompletionResponse completionResponse = JsonConvert.DeserializeObject<CompletionResponse>(response);
                    if (completionResponse != null)
                    {
                        string completionText = completionResponse.Choices?[0]?.Text;
                        if (configData.debug) Puts(completionText);
                        playermessages[userid] = completionText;
                    }
                    else
                    {
                        if (configData.debug) Puts("Request error");
                    }
                },
                Instance,
                RequestMethod.POST,
                headers,
                10f
            );
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["notauthorized"] = "You don't have permission to do that !!",
                ["emptyquestion"] = "Did you have a question?",
                ["noresponse"] = "No response :("
            }, this);
        }

        private void OnServerInitialized()
        {
            permission.RegisterPermission(permUse, this);
            LoadConfigVariables();

            //AddCovalenceCommand("cmd", "DoStuff");
            Instance = this;
        }

        private class ConfigData
        {
            public Options Options;
            public bool debug;
            public VersionNumber Version;
        }

        public class Options
        {
            public string apiKey;
            public string model;
            public string keyWord;
            public bool requirePermission;
            public string ChatIcon;
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            if (string.IsNullOrEmpty(configData.Options.keyWord))
            {
                configData.Options.keyWord = "bot?";
            }
            if (string.IsNullOrEmpty(configData.Options.model))
            {
                configData.Options.model = "text-ada-001"; // Cheapest is text-ada-001
                // text-davinci-003 (expensive), text-curie-001 (less expensive), text-babbage-001 (less so), or text-ada-001 (cheapest)
                // See https://beta.openai.com/docs/models/gpt-3
            }
            configData.Version = Version;
            SaveConfig(configData);
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new ConfigData()
            {
                debug = false,
                Options = new Options()
                {
                    keyWord = "bot?",
                    apiKey = "",
                    requirePermission = true,
                    ChatIcon = "76561199467638159"
                },
                Version = Version
            };

            SaveConfig(config);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        public class CompletionRequest
        {
            [JsonProperty("model")]
            public string Model { get; set; }
            [JsonProperty("prompt")]
            public string Prompt { get; set; }
            [JsonProperty("max_tokens")]
            public int MaxTokens { get; set; }
        }

        public class CompletionResponse
        {
            [JsonProperty("choices")]
            public List<ChatGPTChoice> Choices { get; set; }
            [JsonProperty("usage")]
            public ChatGPTUsage Usage { get; set; }
        }
        public class ChatGPTUsage
        {
            [JsonProperty("prompt_tokens")]
            public int PromptTokens { get; set; }
            [JsonProperty("completion_token")]
            public int CompletionTokens { get; set; }
            [JsonProperty("total_tokens")]
            public int TotalTokens { get; set; }
        }

        [DebuggerDisplay("Text = {Text}")]
        public class ChatGPTChoice
        {
            [JsonProperty("text")]
            public string Text { get; set; }
        }
    }
}
