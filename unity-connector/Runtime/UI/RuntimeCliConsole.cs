using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityCliConnector.Commands;
using UnityCliConnector.Network;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityCliConnector
{
    /// <summary>
    /// Runtime in-game command console for quick local command execution.
    /// </summary>
    public sealed class RuntimeCliConsole : MonoBehaviour, ICommandNotifier
    {
        [Header("UI References")]
        [SerializeField] private InputField _inputField;
        [SerializeField] private Text _historyText;
        [SerializeField] private ScrollRect _suggestionScrollRect;
        [SerializeField] private Button _suggestionItemPrefab;

        [Header("Behavior")]
        [SerializeField] private int _maxHistoryLines = 80;
        [SerializeField] private int _maxCommandHistoryCount = 200;
        [SerializeField] private bool _clearInputAfterExecute = true;
        [SerializeField] private string _hostNameOverride = "";

        private readonly List<string> _historyLines = new();
        private readonly List<string> _executedCommandHistory = new();
        private readonly Dictionary<string, string> _pendingCommandNames = new();
        private readonly List<SuggestionItem> _suggestions = new();
        private readonly List<Button> _suggestionButtons = new();
        private int _historyBrowseIndex = -1;
        private string _historyBrowseDraft = "";
        private bool _suppressBrowseResetOnInputChanged;
        private string _tabCompletionSeed = "";
        private int _tabCompletionIndex = -1;

        private struct SuggestionItem
        {
            public string CommandName;
            public string[] Aliases;
            public string[] ParamDescriptions;
        }

        private void Awake()
        {
            if (_inputField != null)
                _inputField.onValueChanged.AddListener(OnInputChanged);
            RefreshSuggestions(_inputField != null ? _inputField.text : "");
            AppendHistory(FormatHistoryLine("system", $"Runtime CLI ready ({ResolveHostName()})."));
        }

        private void OnDestroy()
        {
            if (_inputField != null)
                _inputField.onValueChanged.RemoveListener(OnInputChanged);
            ClearSuggestionButtons();
        }

        private void Update()
        {
            if (_inputField == null)
                return;

            if (Input.GetKeyDown(KeyCode.UpArrow)) HandleUpKey();
            else if (Input.GetKeyDown(KeyCode.DownArrow)) HandleDownKey();
            else if (Input.GetKeyDown(KeyCode.Tab)) HandleTabCompletion();
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) HandleSubmit();
        }

        private void HandleUpKey()
        {
            NavigateCommandHistory(-1);
        }

        private void HandleDownKey()
        {
            NavigateCommandHistory(1);
        }

        private void OnInputChanged(string value)
        {
            if (!_suppressBrowseResetOnInputChanged)
            {
                ResetHistoryBrowsing();
                ResetTabCompletion();
            }
            RefreshSuggestions(value);
            ScrollSuggestionListToVisualBottom();
        }

        private void HandleTabCompletion()
        {
            if (_inputField == null)
                return;

            var currentText = _inputField.text ?? "";
            var caret = Mathf.Clamp(_inputField.caretPosition, 0, currentText.Length);
            var prefix = currentText[..caret];
            var suffix = currentText[caret..];

            var candidates = BuildTabCandidates(prefix);
            if (candidates.Count == 0)
                return;

            var seed = $"{prefix}|{suffix}";
            if (!string.Equals(_tabCompletionSeed, seed, StringComparison.Ordinal))
            {
                _tabCompletionSeed = seed;
                _tabCompletionIndex = -1;
            }

            var isShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (isShiftPressed)
            {
                _tabCompletionIndex--;
                if (_tabCompletionIndex < 0)
                    _tabCompletionIndex = candidates.Count - 1;
            }
            else
            {
                _tabCompletionIndex++;
                if (_tabCompletionIndex >= candidates.Count)
                    _tabCompletionIndex = 0;
            }

            if (_tabCompletionIndex >= candidates.Count)
                _tabCompletionIndex = 0;

            var selectedPrefix = candidates[_tabCompletionIndex];
            var merged = $"{selectedPrefix}{suffix}";
            ApplyInputText(merged, selectedPrefix.Length);
            RefreshSuggestions(_inputField.text ?? "");
        }

        private List<string> BuildTabCandidates(string currentText)
        {
            var candidates = BuildParamValueTabCandidates(currentText);
            if (candidates.Count > 0)
                return candidates;

            candidates = BuildParameterTabCandidates(currentText);
            if (candidates.Count > 0)
                return candidates;

            if (_suggestions.Count == 0)
                return new List<string>();

            var commandCandidates = new List<string>(_suggestions.Count);
            for (var i = 0; i < _suggestions.Count; i++)
                commandCandidates.Add($"{_suggestions[i].CommandName} ");
            return commandCandidates;
        }

        private List<string> BuildParameterTabCandidates(string currentText)
        {
            var candidates = new List<string>();
            var trimmed = currentText ?? "";
            var tokens = Tokenize(trimmed);
            if (tokens.Count == 0)
                return candidates;

            var commandToken = tokens[0];
            var hostName = ResolveHostName();
            var handler = CommandDiscovery.FindForHost(commandToken, hostName);
            if (handler == null || !string.Equals(handler.Name, commandToken, StringComparison.OrdinalIgnoreCase))
                return candidates;

            var endsWithWhitespace = trimmed.Length > 0 && char.IsWhiteSpace(trimmed[trimmed.Length - 1]);
            var activeToken = (!endsWithWhitespace && tokens.Count > 0) ? tokens[tokens.Count - 1] : "";
            if (!string.IsNullOrEmpty(activeToken) && !activeToken.StartsWith("--", StringComparison.Ordinal))
                return candidates;

            var usedFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.StartsWith("--", StringComparison.Ordinal) && token.Length > 2)
                    usedFlags.Add(token[2..]);
            }

            var fragment = activeToken.StartsWith("--", StringComparison.Ordinal) ? activeToken[2..] : "";
            var descriptions = handler.ParamDescriptions ?? Array.Empty<string>();
            for (var i = 0; i < descriptions.Length; i++)
            {
                var key = ExtractParamKey(descriptions[i]);
                if (string.IsNullOrEmpty(key))
                    continue;
                if (!string.IsNullOrEmpty(fragment) &&
                    !key.StartsWith(fragment, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (usedFlags.Contains(key) && !key.Equals(fragment, StringComparison.OrdinalIgnoreCase))
                    continue;

                candidates.Add(ComposeInputWithParamCompletion(trimmed, activeToken, key));
            }

            return candidates;
        }

        private List<string> BuildParamValueTabCandidates(string currentText)
        {
            var candidates = new List<string>();
            var trimmed = currentText ?? "";
            var tokens = Tokenize(trimmed);
            if (tokens.Count == 0)
                return candidates;

            var commandToken = tokens[0];
            var hostName = ResolveHostName();
            var handler = CommandDiscovery.FindForHost(commandToken, hostName);
            if (handler == null || !string.Equals(handler.Name, commandToken, StringComparison.OrdinalIgnoreCase))
                return candidates;

            var endsWithWhitespace = trimmed.Length > 0 && char.IsWhiteSpace(trimmed[trimmed.Length - 1]);
            if (tokens.Count < 2)
                return candidates;

            string currentValueToken;
            string flagToken;
            if (endsWithWhitespace)
            {
                // Cursor is after a completed token; value slot starts only if previous token is a flag.
                var prev = tokens[tokens.Count - 1];
                if (!prev.StartsWith("--", StringComparison.Ordinal) || prev.Length <= 2)
                    return candidates;
                flagToken = prev;
                currentValueToken = "";
            }
            else
            {
                currentValueToken = tokens[tokens.Count - 1];
                if (currentValueToken.StartsWith("--", StringComparison.Ordinal))
                    return candidates;
                flagToken = tokens.Count >= 2 ? tokens[tokens.Count - 2] : "";
                if (!flagToken.StartsWith("--", StringComparison.Ordinal) || flagToken.Length <= 2)
                    return candidates;
            }

            var paramKey = flagToken[2..];
            var allowedValues = ExtractAllowedValues(handler.ParamDescriptions, paramKey);
            if (allowedValues.Count == 0)
                return candidates;

            // If the value token is already a complete allowed value, do not keep replacing it.
            // Returning empty here allows fallback to parameter-name completion ("forward" tab flow).
            if (!string.IsNullOrEmpty(currentValueToken))
            {
                for (var i = 0; i < allowedValues.Count; i++)
                {
                    if (string.Equals(allowedValues[i], currentValueToken, StringComparison.OrdinalIgnoreCase))
                        return candidates;
                }
            }

            for (var i = 0; i < allowedValues.Count; i++)
            {
                var value = allowedValues[i];
                if (!string.IsNullOrEmpty(currentValueToken) &&
                    !value.StartsWith(currentValueToken, StringComparison.OrdinalIgnoreCase))
                    continue;

                candidates.Add(ComposeInputWithValueCompletion(trimmed, currentValueToken, value));
            }

            return candidates;
        }

        private static string ExtractParamKey(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return "";

            var text = description.TrimStart();
            if (!text.StartsWith("--", StringComparison.Ordinal))
                return "";

            var end = 2;
            while (end < text.Length && !char.IsWhiteSpace(text[end]))
                end++;

            return end > 2 ? text[2..end] : "";
        }

        private static List<string> ExtractAllowedValues(string[] descriptions, string paramKey)
        {
            var values = new List<string>();
            if (descriptions == null || string.IsNullOrWhiteSpace(paramKey))
                return values;

            for (var i = 0; i < descriptions.Length; i++)
            {
                var line = descriptions[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                if (!string.Equals(ExtractParamKey(line), paramKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                var open = line.IndexOf('[', StringComparison.Ordinal);
                var close = line.IndexOf(']', StringComparison.Ordinal);
                if (open < 0 || close <= open + 1)
                    return values;

                var raw = line.Substring(open + 1, close - open - 1);
                var parts = raw.Split('|', StringSplitOptions.RemoveEmptyEntries);
                for (var j = 0; j < parts.Length; j++)
                {
                    var v = parts[j].Trim();
                    if (v.Length == 0)
                        continue;
                    values.Add(v);
                }

                return values;
            }

            return values;
        }

        private static string ComposeInputWithParamCompletion(string currentText, string activeToken, string key)
        {
            var completed = $"--{key}";
            if (!string.IsNullOrEmpty(activeToken))
            {
                var cut = currentText.LastIndexOf(activeToken, StringComparison.Ordinal);
                if (cut >= 0)
                    return $"{currentText[..cut]}{completed} ";
            }

            if (string.IsNullOrWhiteSpace(currentText))
                return $"{completed} ";
            if (char.IsWhiteSpace(currentText[currentText.Length - 1]))
                return $"{currentText}{completed} ";
            return $"{currentText} {completed} ";
        }

        private static string ComposeInputWithValueCompletion(string currentText, string activeValueToken, string value)
        {
            if (!string.IsNullOrEmpty(activeValueToken))
            {
                var cut = currentText.LastIndexOf(activeValueToken, StringComparison.Ordinal);
                if (cut >= 0)
                    return $"{currentText[..cut]}{value}";
            }

            if (string.IsNullOrWhiteSpace(currentText))
                return value;
            if (char.IsWhiteSpace(currentText[currentText.Length - 1]))
                return $"{currentText}{value}";
            return $"{currentText} {value}";
        }

        private void HandleSubmit()
        {
            var raw = _inputField.text ?? "";
            var trimmed = raw.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return;

            RecordExecutedCommand(trimmed);
            ExecuteLine(trimmed);
            if (_clearInputAfterExecute)
                _inputField.text = "";

            _inputField.ActivateInputField();
        }

        private void RecordExecutedCommand(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
                return;

            if (_executedCommandHistory.Count > 0 &&
                string.Equals(
                    _executedCommandHistory[_executedCommandHistory.Count - 1],
                    commandLine,
                    StringComparison.Ordinal))
            {
                ResetHistoryBrowsing();
                ResetTabCompletion();
                return;
            }

            _executedCommandHistory.Add(commandLine);
            var maxCount = Math.Max(1, _maxCommandHistoryCount);
            while (_executedCommandHistory.Count > maxCount)
                _executedCommandHistory.RemoveAt(0);

            ResetHistoryBrowsing();
            ResetTabCompletion();
        }

        private void ResetHistoryBrowsing()
        {
            _historyBrowseIndex = -1;
            _historyBrowseDraft = "";
        }

        private void ResetTabCompletion()
        {
            _tabCompletionSeed = "";
            _tabCompletionIndex = -1;
        }

        private void NavigateCommandHistory(int direction)
        {
            if (_inputField == null || _executedCommandHistory.Count == 0)
                return;

            // Initialize browsing from the current input snapshot.
            if (_historyBrowseIndex < 0)
            {
                _historyBrowseDraft = _inputField.text ?? "";
                _historyBrowseIndex = _executedCommandHistory.Count;
            }

            var nextIndex = _historyBrowseIndex + direction;
            nextIndex = Mathf.Clamp(nextIndex, 0, _executedCommandHistory.Count);
            _historyBrowseIndex = nextIndex;

            var nextText = _historyBrowseIndex == _executedCommandHistory.Count
                ? _historyBrowseDraft
                : _executedCommandHistory[_historyBrowseIndex];

            ApplyInputFromHistory(nextText);
        }

        private void ApplyInputFromHistory(string text)
        {
            var value = text ?? "";
            ApplyInputText(value, value.Length);
        }

        private void ApplyInputText(string text, int caretPosition)
        {
            _suppressBrowseResetOnInputChanged = true;
            _inputField.text = text ?? "";
            _suppressBrowseResetOnInputChanged = false;
            ResetTabCompletion();
            SetCaretPosition(caretPosition);
        }

        private void ExecuteLine(string line)
        {
            if (!TryParseLine(line, out var command, out var parameters, out var parseError))
            {
                AppendHistory(FormatHistoryLine("error", parseError));
                return;
            }

            var requestId = Guid.NewGuid().ToString("N");
            var hostName = ResolveHostName();
            var commandRecord = RuntimeCommandStateManager.Create(hostName, command, CommandCompletionCatalog.GetCompletionKind(command), requestId);
            var context = new CommandContext
            {
                CommandId = commandRecord.Id,
                RequestId = requestId,
                Command = command,
                HostName = hostName,
                Notifier = this,
            };

            _pendingCommandNames[commandRecord.Id] = command;
            AppendHistory(FormatHistoryLine("input", line));

            try
            {
                CommandExecutor.ExecuteCommand(context, parameters);
                if (!context.IsCompleted)
                {
                    AppendHistory(FormatHistoryLine("pending", $"{command} ({commandRecord.Id})"));
                }
            }
            catch (Exception ex)
            {
                AppendHistory(FormatHistoryLine("error", $"{command}: {ex.Message}"));
            }

            RefreshSuggestions(_inputField != null ? _inputField.text : "");
        }

        private static bool TryParseLine(
            string line,
            out string command,
            out Dictionary<string, object> parameters,
            out string error)
        {
            command = null;
            parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            error = null;

            var tokens = Tokenize(line);
            if (tokens.Count == 0)
            {
                error = "Empty command.";
                return false;
            }

            command = tokens[0];
            for (var i = 1; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (!token.StartsWith("--", StringComparison.Ordinal))
                {
                    error = $"Unsupported token '{token}'. Use '--key value'.";
                    return false;
                }

                var key = token[2..];
                if (string.IsNullOrWhiteSpace(key))
                {
                    error = "Parameter key cannot be empty.";
                    return false;
                }

                object value = true;
                if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = ParseValue(tokens[i + 1]);
                    i++;
                }

                if (parameters.TryGetValue(key, out var existing))
                {
                    if (existing is List<object> list)
                    {
                        list.Add(value);
                    }
                    else
                    {
                        parameters[key] = new List<object> { existing, value };
                    }
                }
                else
                {
                    parameters[key] = value;
                }
            }

            foreach (var key in parameters.Keys.ToList())
            {
                if (parameters[key] is List<object> list)
                    parameters[key] = list.Select(v => v?.ToString() ?? "").ToArray();
            }

            return true;
        }

        private static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(input))
                return tokens;

            var sb = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < input.Length; i++)
            {
                var ch = input[i];
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(ch))
                {
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        sb.Clear();
                    }
                    continue;
                }

                sb.Append(ch);
            }

            if (sb.Length > 0)
                tokens.Add(sb.ToString());

            return tokens;
        }

        private static object ParseValue(string raw)
        {
            if (bool.TryParse(raw, out var b))
                return b;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                return i;
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                return f;
            return raw;
        }

        private void RefreshSuggestions(string rawInput)
        {
            _suggestions.Clear();
            var query = (rawInput ?? "").Trim();
            if (string.IsNullOrEmpty(query))
            {
                RebuildSuggestionList();
                return;
            }
            var queryTokens = Tokenize(query);
            var queryCommandToken = queryTokens.Count > 0 ? queryTokens[0] : "";

            var hostName = ResolveHostName();
            foreach (var handler in CommandDiscovery.Handlers)
            {
                if (!CommandAvailability.IsAvailableForHost(handler.Scope, hostName))
                    continue;

                var commandName = handler.Name ?? "";
                if (!IsCommandNameMatch(commandName, queryCommandToken))
                    continue;

                var paramDescriptions = handler.ParamDescriptions ?? Array.Empty<string>();

                _suggestions.Add(new SuggestionItem
                {
                    CommandName = commandName,
                    Aliases = handler.Aliases ?? Array.Empty<string>(),
                    ParamDescriptions = paramDescriptions,
                });
            }

            _suggestions.Sort((a, b) =>
            {
                return string.Compare(a.CommandName, b.CommandName, StringComparison.OrdinalIgnoreCase);
            });
            if (_suggestions.Count == 0)
                RebuildSuggestionList();

            RebuildSuggestionList();
        }

        private static bool IsCommandNameMatch(string commandName, string queryToken)
        {
            if (string.IsNullOrWhiteSpace(queryToken))
                return true;
            if (string.IsNullOrEmpty(commandName))
                return false;

            return commandName.StartsWith(queryToken, StringComparison.OrdinalIgnoreCase);
        }

        private void RebuildSuggestionList()
        {
            if (_suggestionScrollRect == null || _suggestionScrollRect.content == null || _suggestionItemPrefab == null)
                return;

            ClearSuggestionButtons();
            SetSuggestionListVisible(_suggestions.Count > 0);
            if (_suggestions.Count == 0)
                return;

            var suggestionContent = _suggestionScrollRect.content;
            for (var i = 0; i < _suggestions.Count; i++)
            {
                var idx = i;
                var suggestion = _suggestions[i];
                var item = Instantiate(_suggestionItemPrefab, suggestionContent);
                item.gameObject.SetActive(true);

                var label = item.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = BuildSuggestionLabel(suggestion);
                    label.color = new Color(0.85f, 0.85f, 0.85f, 1f);
                }

                var colors = item.colors;
                colors.normalColor = new Color(0.16f, 0.16f, 0.16f, 0.85f);
                colors.selectedColor = colors.normalColor;
                colors.highlightedColor = new Color(0.24f, 0.24f, 0.24f, 0.95f);
                item.colors = colors;
                var nav = item.navigation;
                nav.mode = Navigation.Mode.None;
                item.navigation = nav;

                item.onClick.RemoveAllListeners();
                item.onClick.AddListener(() =>
                {
                    ApplyInputFromHistory(suggestion.CommandName);
                    RefreshSuggestions(_inputField != null ? _inputField.text : "");
                });

                _suggestionButtons.Add(item);
            }
        }

        private void ClearSuggestionButtons()
        {
            for (var i = 0; i < _suggestionButtons.Count; i++)
            {
                var btn = _suggestionButtons[i];
                if (btn == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(btn.gameObject);
                else
                    DestroyImmediate(btn.gameObject);
            }
            _suggestionButtons.Clear();
        }

        private void SetSuggestionListVisible(bool visible)
        {
            if (_suggestionScrollRect != null && _suggestionScrollRect.gameObject.activeSelf != visible)
                _suggestionScrollRect.gameObject.SetActive(visible);
        }

        private void ScrollSuggestionListToVisualBottom()
        {
            if (_suggestionScrollRect == null || _suggestionScrollRect.content == null || _suggestions.Count == 0)
                return;

            Canvas.ForceUpdateCanvases();
            _suggestionScrollRect.verticalNormalizedPosition = 0f;
        }

        private void RestoreInputCaretImmediate()
        {
            if (_inputField == null)
                return;

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_inputField.gameObject);
            _inputField.ActivateInputField();

            var textLength = _inputField.text != null ? _inputField.text.Length : 0;
            _inputField.caretPosition = textLength;
            _inputField.selectionAnchorPosition = textLength;
            _inputField.selectionFocusPosition = textLength;
            _inputField.MoveTextEnd(false);
            _inputField.ForceLabelUpdate();
        }

        private void SetCaretPosition(int position)
        {
            if (_inputField == null)
                return;

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_inputField.gameObject);
            _inputField.ActivateInputField();

            var textLength = _inputField.text != null ? _inputField.text.Length : 0;
            var clamped = Mathf.Clamp(position, 0, textLength);
            _inputField.caretPosition = clamped;
            _inputField.selectionAnchorPosition = clamped;
            _inputField.selectionFocusPosition = clamped;
            _inputField.ForceLabelUpdate();
        }

        private static string BuildSuggestionLabel(SuggestionItem selected)
        {
            var sb = new StringBuilder();
            sb.Append(selected.CommandName);

            if (selected.Aliases != null && selected.Aliases.Length > 0)
            {
                sb.Append("   ");
                sb.Append($"(alias: {string.Join(", ", selected.Aliases)})");
            }

            if (selected.ParamDescriptions.Length > 0)
            {
                var compactParams = new List<string>();
                for (var i = 0; i < selected.ParamDescriptions.Length; i++)
                {
                    var line = selected.ParamDescriptions[i];
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    compactParams.Add(line.Trim());
                }

                if (compactParams.Count > 0)
                {
                    // Keep help-like parameter text, but render in one line to avoid prefab line-clipping.
                    sb.Append("   ");
                    sb.Append(string.Join("   ", compactParams));
                }
            }

            return sb.ToString();
        }

        private void AppendHistory(string line)
        {
            if (_historyText == null)
                return;

            _historyLines.Add(line);
            while (_historyLines.Count > Math.Max(10, _maxHistoryLines))
                _historyLines.RemoveAt(0);

            _historyText.text = string.Join("\n", _historyLines);
        }

        private string ResolveHostName()
        {
            if (!string.IsNullOrWhiteSpace(_hostNameOverride))
                return _hostNameOverride.Trim().ToLowerInvariant();

            return Application.isEditor ? ConnectorHostKind.EditorPlay : ConnectorHostKind.Player;
        }

        private static string ToJsonSafe(object value)
        {
            if (value == null)
                return "null";
            if (value is CommandResult unified)
            {
                if (!string.IsNullOrWhiteSpace(unified.Message))
                    return unified.Message;
                if (unified.Payload != null)
                    return ConnectorJson.Serialize(unified.Payload);
                return string.IsNullOrWhiteSpace(unified.Code) ? "ok" : unified.Code;
            }
            if (value is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("message", out var msg) && msg != null)
                    return msg.ToString();
                if (dict.TryGetValue("error", out var err) && err != null)
                    return err.ToString();
                if (dict.TryGetValue("level", out var level) && level != null)
                    return $"level={level}";
            }
            try
            {
                return ConnectorJson.Serialize(value);
            }
            catch
            {
                return value.ToString();
            }
        }

        private static string FormatHistoryLine(string level, string message)
        {
            var text = message ?? "";
            return level switch
            {
                "input" => $"<color=#9CDCFE>> {text}</color>",
                "ok" => $"<color=#4EC9B0>[ok]</color> {text}",
                "error" => $"<color=#F44747>[error]</color> {text}",
                "pending" => $"<color=#DCDCAA>[pending]</color> {text}",
                "running" => $"<color=#C586C0>[running]</color> {text}",
                "system" => $"<color=#808080>[system]</color> {text}",
                _ => text,
            };
        }

        void ICommandNotifier.MarkRunning(string commandId)
        {
            if (_pendingCommandNames.TryGetValue(commandId, out var command))
                AppendHistory(FormatHistoryLine("running", $"{command} ({commandId})"));
        }

        void ICommandNotifier.Succeed(string commandId, object result)
        {
            if (!_pendingCommandNames.TryGetValue(commandId, out var command))
                command = "unknown";
            AppendHistory(FormatHistoryLine("ok", $"{command}: {ToJsonSafe(result)}"));
            _pendingCommandNames.Remove(commandId);
        }

        void ICommandNotifier.Fail(string commandId, string error)
        {
            if (!_pendingCommandNames.TryGetValue(commandId, out var command))
                command = "unknown";
            AppendHistory(FormatHistoryLine("error", $"{command}: {error}"));
            _pendingCommandNames.Remove(commandId);
        }
    }
}
