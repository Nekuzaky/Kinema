using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Kinema.MotionMatching.Samples
{
    /// <summary>
    /// An <see cref="IAIBrain"/> that asks a language model what the NPC should do next, then hands
    /// the answer to the same <see cref="AICommandProvider"/> any brain uses - so the LLM steers a
    /// character purely through high-level goals and never touches animation.
    ///
    /// It talks to an OpenAI-compatible chat endpoint (endpoint, model and key are all serialized -
    /// nothing is hardcoded, and the key must be supplied by you, never committed). Requests are
    /// async and rate-limited: the model is consulted only when a goal completes or the think
    /// interval elapses - a handful of calls a minute per agent, not per frame - because an LLM in a
    /// per-frame loop is neither affordable nor necessary. Between calls the agent keeps executing
    /// the last command. With no key set, or on any failure, it falls back to a built-in wander so
    /// the agent always behaves.
    ///
    /// This lives in the sample, not the runtime: the runtime stays free of networking and of any
    /// opinion about which model you use.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Samples/LLM AI Brain")]
    public sealed class LLMAIBrain : MonoBehaviour, IAIBrain
    {
        #region Public

        [Header("Endpoint (OpenAI-compatible chat/completions)")]
        [SerializeField] private string _endpoint = "https://api.openai.com/v1/chat/completions";
        [SerializeField] private string _model = "gpt-4o-mini";

        [Tooltip("Your API key. Supply it here at runtime - do NOT commit it. Empty = built-in wander fallback.")]
        [SerializeField] private string _apiKey = "";

        [Header("Behaviour")]
        [Tooltip("A sentence describing who this character is - the LLM's persona and motivation.")]
        [SerializeField, TextArea(2, 4)]
        private string _persona = "A cautious guard patrolling an arena. Keep an eye on the player but keep your distance.";

        [Tooltip("Seconds between model consultations (also consulted the moment a goal is reached).")]
        [SerializeField, Min(2f)] private float _thinkInterval = 8f;

        [Tooltip("Home point for wander goals and the no-key fallback (set to spawn on Start).")]
        [SerializeField, Min(2f)] private float _wanderRadius = 10f;

        public AIAgentCommand Command { get; private set; } = AIAgentCommand.Idle;
        public string Status { get; private set; } = "idle";

        #endregion

        #region Private and Protected

        private Vector3 _home;
        private Transform _player;
        private float _nextThink;
        private bool _requesting;
        private bool _warnedNoKey;

        #endregion

        #region Unity API

        private void Start()
        {
            _home = transform.position;
            Command = Wander("starting out");
        }

        #endregion

        #region IAIBrain

        public void Tick(in AIContext context)
        {
            _player = context.Player;

            // Consult the model on a timer, or the instant the current goal finishes - never per frame.
            bool due = Time.time >= _nextThink || context.ReachedGoal;
            if (!due || _requesting) return;
            _nextThink = Time.time + _thinkInterval;

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_endpoint))
            {
                if (!_warnedNoKey)
                {
                    Debug.Log($"[Kinema] '{name}': no LLM key set, using the built-in wander fallback. " +
                              "Set an API key on the LLM AI Brain to let a model direct this agent.", this);
                    _warnedNoKey = true;
                }
                Command = Wander("no model - wandering");
                return;
            }

            StartCoroutine(Consult(context));
        }

        #endregion

        #region Tools and Utilities — LLM

        private IEnumerator Consult(AIContext context)
        {
            _requesting = true;
            Status = "thinking";

            string body = BuildRequestBody(context);
            using var request = new UnityWebRequest(_endpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + _apiKey);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Kinema] '{name}': LLM request failed ({request.error}); wandering instead.", this);
                Command = Wander("model error - wandering");
                Status = "fallback (error)";
            }
            else if (TryParseCommand(request.downloadHandler.text, out AIAgentCommand command))
            {
                Command = command;
                Status = "llm: " + (string.IsNullOrEmpty(command.Reason) ? command.Goal.ToString() : command.Reason);
            }
            else
            {
                Command = Wander("unparseable reply - wandering");
                Status = "fallback (parse)";
            }

            _requesting = false;
        }

        /// <summary>
        /// A chat request whose system prompt pins the character and forces a single JSON object back.
        /// The response schema is deliberately tiny - goal, a target point, a speed and a reason -
        /// which is all a locomotion agent needs and all a model can reliably produce.
        /// </summary>
        private string BuildRequestBody(AIContext context)
        {
            Vector3 p = context.Position;
            Vector3 pl = context.Player != null ? context.Player.position : p;

            string system = _persona + "\n" +
                "You direct a character in a 3D arena by choosing ONE goal. Reply with ONLY a JSON " +
                "object, no prose: {\"goal\":\"Idle|MoveTo|Follow|Flee|Wander\", \"x\":float, \"z\":float, " +
                "\"speed\":0..1, \"reason\":\"short\"}. x,z is a world point for MoveTo. Follow/Flee act " +
                "on the player. Keep reasons under 8 words.";

            string user = $"You are at ({p.x:F1},{p.z:F1}). The player is at ({pl.x:F1},{pl.z:F1}), " +
                          $"{context.DistanceToPlayer:F1} m away. Home is ({_home.x:F1},{_home.z:F1}). " +
                          "Choose the next goal.";

            var sb = new StringBuilder();
            sb.Append("{\"model\":").Append(JsonString(_model));
            sb.Append(",\"temperature\":0.7,\"messages\":[");
            sb.Append("{\"role\":\"system\",\"content\":").Append(JsonString(system)).Append("},");
            sb.Append("{\"role\":\"user\",\"content\":").Append(JsonString(user)).Append("}]}");
            return sb.ToString();
        }

        /// <summary>
        /// Pulls the assistant text out of an OpenAI-shaped response, then the command JSON out of
        /// that text (models sometimes wrap it), and maps it to a command.
        /// </summary>
        private bool TryParseCommand(string response, out AIAgentCommand command)
        {
            command = AIAgentCommand.Idle;

            string content = ExtractAssistantContent(response);
            if (content == null) return false;

            int open = content.IndexOf('{');
            int close = content.LastIndexOf('}');
            if (open < 0 || close <= open) return false;
            string json = content.Substring(open, close - open + 1);

            LlmCommandDto dto;
            try { dto = JsonUtility.FromJson<LlmCommandDto>(json); }
            catch { return false; }
            if (dto == null || string.IsNullOrEmpty(dto.goal)) return false;

            float speed = Mathf.Clamp01(dto.speed <= 0f ? 1f : dto.speed);
            switch (dto.goal.Trim().ToLowerInvariant())
            {
                case "moveto": command = AIAgentCommand.MoveTo(new Vector3(dto.x, 0f, dto.z), speed, dto.reason); return true;
                case "wander": command = Wander(dto.reason); return true;
                case "follow": command = AIAgentCommand.FollowTarget(_player, speed, dto.reason); return true;
                case "flee":
                    command = new AIAgentCommand { Goal = AIGoal.Flee, Target = _player, SpeedScale = speed, Reason = dto.reason };
                    return true;
                case "idle": command = AIAgentCommand.Idle; command.Reason = dto.reason; return true;
                default: return false;
            }
        }

        /// <summary>Minimal extraction of choices[0].message.content without a JSON library.</summary>
        private static string ExtractAssistantContent(string response)
        {
            const string marker = "\"content\":";
            int i = response.IndexOf(marker, System.StringComparison.Ordinal);
            if (i < 0) return null;
            i += marker.Length;
            while (i < response.Length && response[i] != '"') i++;
            if (i >= response.Length) return null;
            i++;

            var sb = new StringBuilder();
            for (; i < response.Length; i++)
            {
                char c = response[i];
                if (c == '\\' && i + 1 < response.Length)
                {
                    char n = response[++i];
                    sb.Append(n switch { 'n' => '\n', 't' => '\t', '"' => '"', '\\' => '\\', _ => n });
                }
                else if (c == '"') break;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private AIAgentCommand Wander(string reason)
        {
            Vector2 disc = Random.insideUnitCircle * _wanderRadius;
            return AIAgentCommand.MoveTo(_home + new Vector3(disc.x, 0f, disc.y), 0.7f, reason);
        }

        private static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
                sb.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\t' => "\\t", '\r' => "", _ => c.ToString() });
            return sb.Append('"').ToString();
        }

        [System.Serializable]
        private sealed class LlmCommandDto
        {
            public string goal;
            public float x, z, speed;
            public string reason;
        }

        #endregion
    }
}
