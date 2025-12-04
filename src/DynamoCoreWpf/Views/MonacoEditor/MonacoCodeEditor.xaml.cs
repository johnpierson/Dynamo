using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Dynamo.Controls;
using Dynamo.Models;
using Dynamo.ViewModels;
using Dynamo.Wpf.Utilities;
using DynamoUtilities;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf; // Add this using directive

namespace Dynamo.UI.Controls
{
    /// <summary>
    /// Monaco Editor language types supported by this control.
    /// </summary>
    public enum MonacoLanguage
    {
        Python,
        CSharp,
        DesignScript,
        CodeBlock
    }

    /// <summary>
    /// Event args for content changed events from Monaco Editor.
    /// </summary>
    public class MonacoContentChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The new content of the editor.
        /// </summary>
        public string Content { get; }

        /// <summary>
        /// Creates a new instance of MonacoContentChangedEventArgs.
        /// </summary>
        public MonacoContentChangedEventArgs(string content)
        {
            Content = content;
        }
    }

    /// <summary>
    /// Event args for completion request events from Monaco Editor.
    /// </summary>
    public class MonacoCompletionRequestEventArgs : EventArgs
    {
        /// <summary>
        /// The request ID to match with the response.
        /// </summary>
        public string RequestId { get; }

        /// <summary>
        /// The code up to the cursor position.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// The line number of the cursor.
        /// </summary>
        public int Line { get; }

        /// <summary>
        /// The column number of the cursor.
        /// </summary>
        public int Column { get; }

        /// <summary>
        /// Creates a new instance of MonacoCompletionRequestEventArgs.
        /// </summary>
        public MonacoCompletionRequestEventArgs(string requestId, string code, int line, int column)
        {
            RequestId = requestId;
            Code = code;
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// Represents a completion item for Monaco Editor.
    /// </summary>
    public class MonacoCompletionItem
    {
        /// <summary>
        /// The label to display in the completion list.
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// The text to insert when this completion is selected.
        /// </summary>
        public string InsertText { get; set; }

        /// <summary>
        /// The kind of completion item (Method, Class, Property, etc.).
        /// </summary>
        public string Kind { get; set; }

        /// <summary>
        /// Additional detail about the completion item.
        /// </summary>
        public string Detail { get; set; }

        /// <summary>
        /// Documentation for the completion item.
        /// </summary>
        public string Documentation { get; set; }

        /// <summary>
        /// The text to use for sorting.
        /// </summary>
        public string SortText { get; set; }
    }

    /// <summary>
    /// Event args for hover request events from Monaco Editor.
    /// </summary>
    public class MonacoHoverRequestEventArgs : EventArgs
    {
        /// <summary>
        /// The request ID to match with the response.
        /// </summary>
        public string RequestId { get; }

        /// <summary>
        /// The code up to the cursor position.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// The word at the cursor position.
        /// </summary>
        public string Word { get; }

        /// <summary>
        /// The line number of the cursor.
        /// </summary>
        public int Line { get; }

        /// <summary>
        /// The column number of the cursor.
        /// </summary>
        public int Column { get; }

        /// <summary>
        /// Creates a new instance of MonacoHoverRequestEventArgs.
        /// </summary>
        public MonacoHoverRequestEventArgs(string requestId, string code, string word, int line, int column)
        {
            RequestId = requestId;
            Code = code;
            Word = word;
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// Event args for signature help request events from Monaco Editor.
    /// </summary>
    public class MonacoSignatureRequestEventArgs : EventArgs
    {
        /// <summary>
        /// The request ID to match with the response.
        /// </summary>
        public string RequestId { get; }

        /// <summary>
        /// The code up to the cursor position.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// The function name being called.
        /// </summary>
        public string FunctionName { get; }

        /// <summary>
        /// The line number of the cursor.
        /// </summary>
        public int Line { get; }

        /// <summary>
        /// The column number of the cursor.
        /// </summary>
        public int Column { get; }

        /// <summary>
        /// Creates a new instance of MonacoSignatureRequestEventArgs.
        /// </summary>
        public MonacoSignatureRequestEventArgs(string requestId, string code, string functionName, int line, int column)
        {
            RequestId = requestId;
            Code = code;
            FunctionName = functionName;
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// A code editor control powered by Monaco Editor (VS Code's editor) via WebView2.
    /// This is an experimental feature available only in debug builds.
    /// </summary>
    public partial class MonacoCodeEditor : UserControl, IDisposable
    {
        private static readonly string HtmlResourceName = "Dynamo.Wpf.Views.MonacoEditor.MonacoEditorHost.html";
        
        private DynamoWebView2 webView;
        private bool isInitialized;
        private bool isDisposed;
        private string pendingContent;
        private MonacoLanguage pendingLanguage = MonacoLanguage.Python;
        private NodeViewModel nodeViewModel;
        private DynamoViewModel dynamoViewModel;

        /// <summary>
        /// Event raised when the editor content changes.
        /// </summary>
        public event EventHandler<MonacoContentChangedEventArgs> ContentChanged;

        /// <summary>
        /// Event raised when the editor gains focus.
        /// </summary>
        public event EventHandler EditorFocused;

        /// <summary>
        /// Event raised when the editor loses focus.
        /// </summary>
        public event EventHandler EditorBlurred;

        /// <summary>
        /// Event raised when the editor is fully initialized and ready.
        /// </summary>
        public event EventHandler EditorReady;

        /// <summary>
        /// Event raised when the editor requests code completions.
        /// </summary>
        public event EventHandler<MonacoCompletionRequestEventArgs> CompletionRequested;

        /// <summary>
        /// Event raised when the editor requests hover information.
        /// </summary>
        public event EventHandler<MonacoHoverRequestEventArgs> HoverRequested;

        /// <summary>
        /// Event raised when the editor requests signature help.
        /// </summary>
        public event EventHandler<MonacoSignatureRequestEventArgs> SignatureRequested;

        /// <summary>
        /// Gets whether the Monaco Editor is fully initialized.
        /// </summary>
        public bool IsEditorReady => isInitialized;

        /// <summary>
        /// Gets or sets the current language mode.
        /// </summary>
        public MonacoLanguage Language
        {
            get => pendingLanguage;
            set
            {
                pendingLanguage = value;
                if (isInitialized)
                {
                    _ = SetLanguageAsync(value);
                }
            }
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        public MonacoCodeEditor()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Constructor with NodeView for integration with Dynamo nodes.
        /// </summary>
        /// <param name="nodeView">The NodeView this editor is attached to.</param>
        public MonacoCodeEditor(NodeView nodeView) : this()
        {
            if (nodeView != null)
            {
                nodeView.Unloaded += (obj, args) => Dispose();
                this.nodeViewModel = nodeView.ViewModel;
                this.DataContext = nodeViewModel?.NodeModel;
                this.dynamoViewModel = nodeViewModel?.DynamoViewModel;
            }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (isDisposed || isInitialized)
                return;

            await InitializeWebViewAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Dispose();
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                webView = new DynamoWebView2
                {
                    Margin = new Thickness(0),
                    ZoomFactor = 1.0
                };

                // Set up user data folder for WebView2
                var userDataDir = GetUserDataDirectory();
                PathHelper.CreateFolderIfNotExist(userDataDir);

                webView.CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = DynamoModel.IsTestMode 
                        ? TestUtilities.UserDataFolderDuringTests(nameof(MonacoCodeEditor)) 
                        : userDataDir
                };

                HostGrid.Children.Add(webView);

                await webView.Initialize(LogMessage);

                // Configure WebView2 settings
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
#if DEBUG
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
#else
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif

                // Listen for messages from JavaScript
                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                // Load the Monaco Editor HTML
                var htmlContent = LoadHtmlResource();
                if (!string.IsNullOrEmpty(htmlContent))
                {
                    webView.NavigateToString(htmlContent);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to initialize Monaco Editor: {ex.Message}");
            }
        }

        private string GetUserDataDirectory()
        {
            var version = Dynamo.Utilities.AssemblyHelper.GetDynamoVersion();
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(folder, "Dynamo", "Dynamo Core",
                $"{version.Major}.{version.Minor}", "MonacoEditor");
        }

        private string LoadHtmlResource()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(HtmlResourceName))
                {
                    if (stream == null)
                    {
                        LogMessage($"Could not find embedded resource: {HtmlResourceName}");
                        return null;
                    }

                    using (var reader = new StreamReader(stream))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading HTML resource: {ex.Message}");
                return null;
            }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var message = e.WebMessageAsJson;
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeElement))
                    return;

                var messageType = typeElement.GetString();

                switch (messageType)
                {
                    case "editorReady":
                        OnEditorReady();
                        break;

                    case "contentChanged":
                        if (root.TryGetProperty("content", out var contentElement))
                        {
                            var content = contentElement.GetString();
                            ContentChanged?.Invoke(this, new MonacoContentChangedEventArgs(content));
                        }
                        break;

                    case "editorFocused":
                        EditorFocused?.Invoke(this, EventArgs.Empty);
                        break;

                    case "editorBlurred":
                        EditorBlurred?.Invoke(this, EventArgs.Empty);
                        break;

                    case "cursorChanged":
                        // Can be used for status bar updates if needed
                        break;

                    case "requestCompletions":
                        if (root.TryGetProperty("requestId", out var requestIdElement) &&
                            root.TryGetProperty("code", out var codeElement) &&
                            root.TryGetProperty("line", out var lineElement) &&
                            root.TryGetProperty("column", out var columnElement))
                        {
                            var requestId = requestIdElement.GetString();
                            var code = codeElement.GetString();
                            var line = lineElement.GetInt32();
                            var column = columnElement.GetInt32();
                            
                            LogMessage($"Completion request: requestId={requestId}, code length={code?.Length ?? 0}, line={line}, column={column}");
                            if (!string.IsNullOrEmpty(code) && code.Length > 0)
                            {
                                var lastChars = code.Length > 50 ? code.Substring(code.Length - 50) : code;
                                LogMessage($"Last 50 chars of code: {lastChars}");
                            }
                            
                            CompletionRequested?.Invoke(this, new MonacoCompletionRequestEventArgs(requestId, code, line, column));
                        }
                        break;

                    case "requestHover":
                        if (root.TryGetProperty("requestId", out var hoverRequestIdElement) &&
                            root.TryGetProperty("code", out var hoverCodeElement) &&
                            root.TryGetProperty("word", out var wordElement) &&
                            root.TryGetProperty("line", out var hoverLineElement) &&
                            root.TryGetProperty("column", out var hoverColumnElement))
                        {
                            var requestId = hoverRequestIdElement.GetString();
                            var code = hoverCodeElement.GetString();
                            var word = wordElement.GetString();
                            var line = hoverLineElement.GetInt32();
                            var column = hoverColumnElement.GetInt32();
                            
                            HoverRequested?.Invoke(this, new MonacoHoverRequestEventArgs(requestId, code, word, line, column));
                        }
                        break;

                    case "requestSignature":
                        if (root.TryGetProperty("requestId", out var sigRequestIdElement) &&
                            root.TryGetProperty("code", out var sigCodeElement) &&
                            root.TryGetProperty("functionName", out var functionNameElement) &&
                            root.TryGetProperty("line", out var sigLineElement) &&
                            root.TryGetProperty("column", out var sigColumnElement))
                        {
                            var requestId = sigRequestIdElement.GetString();
                            var code = sigCodeElement.GetString();
                            var functionName = functionNameElement.GetString();
                            var line = sigLineElement.GetInt32();
                            var column = sigColumnElement.GetInt32();
                            
                            SignatureRequested?.Invoke(this, new MonacoSignatureRequestEventArgs(requestId, code, functionName, line, column));
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error processing web message: {ex.Message}");
            }
        }

        private async void OnEditorReady()
        {
            isInitialized = true;
            LoadingText.Visibility = Visibility.Collapsed;

            // Apply pending settings
            if (!string.IsNullOrEmpty(pendingContent))
            {
                await SetContentAsync(pendingContent);
                pendingContent = null;
            }

            await SetLanguageAsync(pendingLanguage);

            EditorReady?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Sets the content of the editor.
        /// </summary>
        /// <param name="content">The code content to set.</param>
        public async Task SetContentAsync(string content)
        {
            if (!isInitialized)
            {
                pendingContent = content;
                return;
            }

            try
            {
                var escapedContent = JsonSerializer.Serialize(content ?? string.Empty);
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.monacoApi.setContent({escapedContent})");
            }
            catch (Exception ex)
            {
                LogMessage($"Error setting content: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current content of the editor.
        /// </summary>
        /// <returns>The current code content.</returns>
        public async Task<string> GetContentAsync()
        {
            if (!isInitialized)
                return pendingContent ?? string.Empty;

            try
            {
                var result = await webView.CoreWebView2.ExecuteScriptAsync(
                    "window.monacoApi.getContent()");
                
                // Result is a JSON string, so we need to deserialize it
                return JsonSerializer.Deserialize<string>(result) ?? string.Empty;
            }
            catch (Exception ex)
            {
                LogMessage($"Error getting content: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Sets the language mode of the editor.
        /// </summary>
        /// <param name="language">The language to set.</param>
        public async Task SetLanguageAsync(MonacoLanguage language)
        {
            pendingLanguage = language;

            if (!isInitialized)
                return;

            try
            {
                var languageString = language.ToString().ToLowerInvariant();
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.monacoApi.setLanguage('{languageString}')");
            }
            catch (Exception ex)
            {
                LogMessage($"Error setting language: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the editor to read-only mode.
        /// </summary>
        /// <param name="readOnly">True to make the editor read-only.</param>
        public async Task SetReadOnlyAsync(bool readOnly)
        {
            if (!isInitialized)
                return;

            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.monacoApi.setReadOnly({readOnly.ToString().ToLowerInvariant()})");
            }
            catch (Exception ex)
            {
                LogMessage($"Error setting read-only: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the font size of the editor.
        /// </summary>
        /// <param name="fontSize">The font size in pixels.</param>
        public async Task SetFontSizeAsync(int fontSize)
        {
            if (!isInitialized)
                return;

            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.monacoApi.setFontSize({fontSize})");
            }
            catch (Exception ex)
            {
                LogMessage($"Error setting font size: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows or hides line numbers.
        /// </summary>
        /// <param name="show">True to show line numbers.</param>
        public async Task SetLineNumbersAsync(bool show)
        {
            if (!isInitialized)
                return;

            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.monacoApi.setLineNumbers({show.ToString().ToLowerInvariant()})");
            }
            catch (Exception ex)
            {
                LogMessage($"Error setting line numbers: {ex.Message}");
            }
        }

        /// <summary>
        /// Focuses the editor.
        /// </summary>
        public async Task FocusEditorAsync()
        {
            if (!isInitialized)
                return;

            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(
                    "window.monacoApi.focus()");
            }
            catch (Exception ex)
            {
                LogMessage($"Error focusing editor: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs an undo operation.
        /// </summary>
        public async Task UndoAsync()
        {
            if (!isInitialized)
                return;

            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(
                    "window.monacoApi.undo()");
            }
            catch (Exception ex)
            {
                LogMessage($"Error performing undo: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs a redo operation.
        /// </summary>
        public async Task RedoAsync()
        {
            if (!isInitialized)
                return;

            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(
                    "window.monacoApi.redo()");
            }
            catch (Exception ex)
            {
                LogMessage($"Error performing redo: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends completion suggestions back to the Monaco Editor.
        /// </summary>
        /// <param name="requestId">The request ID to match with the original request.</param>
        /// <param name="completions">The list of completion items.</param>
        public async Task SendCompletionsAsync(string requestId, List<MonacoCompletionItem> completions)
        {
            if (!isInitialized || webView?.CoreWebView2 == null)
                return;

            try
            {
                var completionsJson = JsonSerializer.Serialize(completions);
                var requestIdJson = JsonSerializer.Serialize(requestId);
                var script = $@"
                    (function() {{
                        if (window.handleCompletionResponse) {{
                            window.handleCompletionResponse({requestIdJson}, {completionsJson});
                        }}
                    }})();
                ";
                await webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                LogMessage($"Error sending completions: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends hover information back to the Monaco Editor.
        /// </summary>
        /// <param name="requestId">The request ID to match with the original request.</param>
        /// <param name="contents">The hover contents (markdown string or array of markdown strings).</param>
        public async Task SendHoverAsync(string requestId, string contents)
        {
            if (!isInitialized || webView?.CoreWebView2 == null)
                return;

            try
            {
                var contentsJson = JsonSerializer.Serialize(contents);
                var requestIdJson = JsonSerializer.Serialize(requestId);
                var hoverInfo = $@"{{ contents: [{{ value: {contentsJson}, kind: 'markdown' }}] }}";
                var script = $@"
                    (function() {{
                        if (window.handleHoverResponse) {{
                            window.handleHoverResponse({requestIdJson}, {hoverInfo});
                        }}
                    }})();
                ";
                await webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                LogMessage($"Error sending hover: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends signature help information back to the Monaco Editor.
        /// </summary>
        /// <param name="requestId">The request ID to match with the original request.</param>
        /// <param name="signatures">The list of function signatures.</param>
        /// <param name="activeSignature">The index of the active signature.</param>
        /// <param name="activeParameter">The index of the active parameter.</param>
        public async Task SendSignatureHelpAsync(string requestId, List<string> signatures, int activeSignature = 0, int activeParameter = 0)
        {
            if (!isInitialized || webView?.CoreWebView2 == null)
                return;

            try
            {
                var sigs = signatures.Select(s => new { label = s, documentation = "" }).ToArray();
                var sigsJson = JsonSerializer.Serialize(sigs);
                var requestIdJson = JsonSerializer.Serialize(requestId);
                var signatureInfo = $@"{{
                    signatures: {sigsJson},
                    activeSignature: {activeSignature},
                    activeParameter: {activeParameter}
                }}";
                var script = $@"
                    (function() {{
                        if (window.handleSignatureResponse) {{
                            window.handleSignatureResponse({requestIdJson}, {signatureInfo});
                        }}
                    }})();
                ";
                await webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                LogMessage($"Error sending signature help: {ex.Message}");
            }
        }

        private void LogMessage(string message)
        {
            dynamoViewModel?.Model?.Logger?.Log(message);
            System.Diagnostics.Debug.WriteLine($"[MonacoCodeEditor] {message}");
        }

        /// <summary>
        /// Disposes the Monaco Editor control and releases WebView2 resources.
        /// </summary>
        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;

            if (webView != null)
            {
                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                }
                webView.Dispose();
                webView = null;
            }
        }
    }
}

