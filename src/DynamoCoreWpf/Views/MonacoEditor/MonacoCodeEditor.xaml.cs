using System;
using System.IO;
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

