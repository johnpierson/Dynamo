using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using Dynamo.Configuration;
using Dynamo.Controls;
using Dynamo.Logging;
using Dynamo.Models;
using Dynamo.Python;
using Dynamo.PythonServices;
using Dynamo.Utilities;
using Dynamo.ViewModels;
using Dynamo.Wpf.Views;
using Dynamo.Wpf.Windows;
using Dynamo.UI.Controls;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using PythonNodeModels;

namespace PythonNodeModelsWpf
{
    /// <summary>
    /// Interaction logic for ScriptEditorWindow.xaml
    /// </summary>
    public partial class ScriptEditorWindow : ModelessChildWindow
    {
        #region Private properties
        private string propertyName = string.Empty;
        private Guid boundNodeId = Guid.Empty;
        private Guid boundWorkspaceId = Guid.Empty;
        private CompletionWindow completionWindow = null;
        private readonly SharedCompletionProvider completionProvider;
        private readonly DynamoViewModel dynamoViewModel;
        private bool nodeWasModified = false;
        private string originalScript;
        internal FoldingManager foldingManager;
        private TabFoldingStrategy foldingStrategy;
        private int zoomScaleCacheValue;

        private readonly double fontSizePreferencesSliderProportionValue = (FONT_MAX_SIZE - FONT_MIN_SIZE) / (pythonZoomScalingSliderMaximum - pythonZoomScalingSliderMinimum);

        // Reasonable max and min font size values for zooming limits
        private const double FONT_MAX_SIZE = 60d;
        private const double FONT_MIN_SIZE = 5d;

        private const double pythonZoomScalingSliderMaximum = 300d;
        private const double pythonZoomScalingSliderMinimum = 25d;
        private bool isSaved = true;
        private bool useMonacoEditor;
        private MonacoCodeEditor MonacoEditor;
        #endregion

        /// <summary>
        /// Flag to check if the editor window is saved.
        /// </summary>
        internal bool IsSaved
        {
            get { return isSaved; }
            set
            {
                isSaved = value;
                if (!useMonacoEditor)
                {
                    editText.IsModified = !IsSaved;
                }
                NodeModel.ScriptContentSaved = isSaved;
            }
        }

        private async Task<string> GetEditorTextAsync()
        {
            if (useMonacoEditor && MonacoEditor != null)
            {
                return await MonacoEditor.GetContentAsync();
            }
            return editText.Text;
        }

        private async Task SetEditorTextAsync(string text)
        {
            if (useMonacoEditor && MonacoEditor != null)
            {
                await MonacoEditor.SetContentAsync(text);
            }
            else
            {
                editText.Text = text;
            }
        }

        #region Public properties
        /// <summary>
        /// Python node model
        /// </summary>
        public PythonNode NodeModel { get; set; }

        /// <summary>
        /// Cached Python Engine value
        /// </summary>
        public string CachedEngine { get; set; }

        /// <summary>
        /// Available Python engines.
        /// </summary>
        public ObservableCollection<string> AvailableEngines {
            get; private set;
        }
        #endregion
        public ScriptEditorWindow(
            DynamoViewModel dynamoViewModel,
            PythonNode nodeModel,
            NodeView nodeView,
            ref ModelessChildWindow.WindowRect windowRect
        ) : base(nodeView, ref windowRect)
        {
            Closed += OnScriptEditorWindowClosed;
            this.dynamoViewModel = dynamoViewModel;
            this.NodeModel = nodeModel;

            completionProvider = new SharedCompletionProvider(nodeModel.EngineName,
                dynamoViewModel.Model.PathManager.DynamoCoreDirectory);
            completionProvider.MessageLogged += dynamoViewModel.Model.Logger.Log;
            nodeModel.CodeMigrated += OnNodeModelCodeMigrated;

            InitializeComponent();
            DataContext = this;

            EngineSelectorComboBox.Visibility = Visibility.Visible;
            NodeModel.UserScriptWarned += WarnUserScript;

            Analytics.TrackScreenView("Python");
        }

        private void PythonZoomScalingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var slider = (Slider)sender;

            bool shouldIncrease = slider.Value > zoomScaleCacheValue;

            double deltaValue = fontSizePreferencesSliderProportionValue * Math.Abs(slider.Value - zoomScaleCacheValue);
            UpdateFontSize(shouldIncrease, deltaValue);
            zoomScaleCacheValue = (int)slider.Value;
            dynamoViewModel.PreferenceSettings.PythonScriptZoomScale = (int)slider.Value;
        }

        internal void Initialize(Guid workspaceGuid, Guid nodeGuid, string propName, string propValue)
        {
            boundWorkspaceId = workspaceGuid;
            boundNodeId = nodeGuid;
            propertyName = propName;

            // Check if Monaco Editor preference is enabled
            useMonacoEditor = dynamoViewModel.PreferenceSettings.UseMonacoEditor;

            if (useMonacoEditor)
            {
                InitializeMonacoEditor(propValue);
            }
            else
            {
                InitializeAvalonEditor(propValue);
            }

            AvailableEngines =
                new ObservableCollection<string>(PythonEngineManager.Instance.AvailableEngines.Select(x => x.Name));
            // Add the serialized Python Engine even if it is missing (so that the user does not see an empty slot)
            if (!AvailableEngines.Contains(NodeModel.EngineName))
            {
                AvailableEngines.Add(NodeModel.EngineName);
            }

            PythonEngineManager.Instance.AvailableEngines.CollectionChanged += UpdateAvailableEngines;

            originalScript = propValue;
            CachedEngine = NodeModel.EngineName;
            EngineSelectorComboBox.ItemsSource = AvailableEngines;
            EngineSelectorComboBox.SelectedItem = CachedEngine;

            if (!useMonacoEditor)
            {
                InstallFoldingManager();
            }

            dynamoViewModel.PreferencesWindowChanged += DynamoViewModel_PreferencesWindowChanged;

            dynamoViewModel.PreferenceSettings.PropertyChanged += PreferenceSettings_PropertyChanged;
            NodeModel.PropertyChanged += OnNodeModelPropertyChanged;

            UpdatePythonUpgradeBar();
            UpdateMigrationAssistantButtonEnabled();
        }

        private void InitializeAvalonEditor(string propValue)
        {
            // Register auto-completion callbacks
            editText.TextArea.TextEntering += OnTextAreaTextEntering;
            editText.TextArea.TextEntered += OnTextAreaTextEntered;

            // Hyperlink color
            editText.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(Color.FromArgb(255, 106, 192, 231));

            // Initialize editor with global settings for show/hide tabs and spaces
            editText.Options = dynamoViewModel.PythonScriptEditorTextOptions.GetTextOptions();

            // Hyperlink color
            editText.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(Color.FromArgb(255, 106, 192, 231));

            // Set options to reflect global settings when python script editor in initialized for the first time.
            editText.Options.ShowSpaces = dynamoViewModel.ShowTabsAndSpacesInScriptEditor;
            editText.Options.ShowTabs = dynamoViewModel.ShowTabsAndSpacesInScriptEditor;

            // Set font size in editor and cache it
            editText.FontSize = dynamoViewModel.PreferenceSettings.PythonScriptZoomScale * fontSizePreferencesSliderProportionValue;
            zoomScaleCacheValue = dynamoViewModel.PreferenceSettings.PythonScriptZoomScale;

            const string highlighting = "ICSharpCode.PythonBinding.Resources.Python.xshd";
            var elem = GetType().Assembly.GetManifestResourceStream(
                "PythonNodeModelsWpf.Resources." + highlighting);

            editText.SyntaxHighlighting = HighlightingLoader.Load(
                new XmlTextReader(elem), HighlightingManager.Instance);

            // Add custom highlighting rules consistent with DesignScript
            CodeHighlightingRuleFactory.AddCommonHighlighingRules(editText, dynamoViewModel.EngineController);

            editText.Text = propValue;

            // Show Avalon editor, hide Monaco
            AvalonEditorBorder.Visibility = Visibility.Visible;
            MonacoEditorBorder.Visibility = Visibility.Collapsed;
        }

        private void InitializeMonacoEditor(string propValue)
        {
            // Hide Avalon editor, show Monaco
            AvalonEditorBorder.Visibility = Visibility.Collapsed;
            MonacoEditorBorder.Visibility = Visibility.Visible;

            // Initialize Monaco editor
            MonacoEditor = new MonacoCodeEditor();
            MonacoEditor.Language = MonacoLanguage.Python;
            MonacoEditor.HorizontalAlignment = HorizontalAlignment.Stretch;
            MonacoEditor.VerticalAlignment = VerticalAlignment.Stretch;
            MonacoEditorBorder.Child = MonacoEditor;

            // Wire up Monaco editor events
            MonacoEditor.ContentChanged += OnMonacoContentChanged;
            MonacoEditor.EditorReady += OnMonacoEditorReady;
            MonacoEditor.CompletionRequested += OnMonacoCompletionRequested;
            MonacoEditor.HoverRequested += OnMonacoHoverRequested;
            MonacoEditor.SignatureRequested += OnMonacoSignatureRequested;

            // Set initial content
            _ = MonacoEditor.SetContentAsync(propValue);

            // Apply preferences
            _ = ApplyMonacoPreferencesAsync();
        }

        private async void OnMonacoCompletionRequested(object sender, MonacoCompletionRequestEventArgs e)
        {
            try
            {
                dynamoViewModel.Model.Logger.Log($"=== Monaco Completion Request START ===");
                dynamoViewModel.Model.Logger.Log($"RequestId: {e.RequestId}");
                dynamoViewModel.Model.Logger.Log($"Code length: {e.Code?.Length ?? 0}");
                dynamoViewModel.Model.Logger.Log($"Line: {e.Line}, Column: {e.Column}");
                
                if (completionProvider == null)
                {
                    dynamoViewModel.Model.Logger.Log("ERROR: Monaco completion: completionProvider is null");
                    await MonacoEditor.SendCompletionsAsync(e.RequestId, new System.Collections.Generic.List<MonacoCompletionItem>());
                    return;
                }

                if (!completionProvider.IsReady)
                {
                    dynamoViewModel.Model.Logger.Log("ERROR: Monaco completion: completionProvider is not ready (no matching Python engine found)");
                    await MonacoEditor.SendCompletionsAsync(e.RequestId, new System.Collections.Generic.List<MonacoCompletionItem>());
                    return;
                }

                // Log the code being sent for debugging
                if (!string.IsNullOrEmpty(e.Code) && e.Code.Length > 0)
                {
                    var lastChars = e.Code.Length > 150 ? e.Code.Substring(e.Code.Length - 150) : e.Code;
                    dynamoViewModel.Model.Logger.Log($"Last 150 chars of code: {lastChars}");
                    dynamoViewModel.Model.Logger.Log($"Full code ends with: ...{e.Code.Substring(Math.Max(0, e.Code.Length - 20))}");
                }
                else
                {
                    dynamoViewModel.Model.Logger.Log("WARNING: Code is null or empty!");
                }

                // Get completions from Python completion provider
                // The code should include everything up to and including the cursor position (including the '.' character)
                // This matches AvalonEdit's behavior: editText.Text.Substring(0, editText.CaretOffset)
                dynamoViewModel.Model.Logger.Log($"Calling completionProvider.GetCompletionData (Engine: {NodeModel?.EngineName ?? "unknown"})...");
                var completions = completionProvider.GetCompletionData(e.Code, false);
                
                dynamoViewModel.Model.Logger.Log($"Monaco completion: received {completions?.Length ?? 0} completions");
                if (completions != null && completions.Length > 0)
                {
                    var firstFew = string.Join(", ", completions.Take(10).Select(c => c.Text));
                    dynamoViewModel.Model.Logger.Log($"First 10 completions: {firstFew}");
                }
                else
                {
                    dynamoViewModel.Model.Logger.Log("WARNING: No completions returned from provider!");
                }
                
                if (completions == null || completions.Length == 0)
                {
                    dynamoViewModel.Model.Logger.Log("Sending empty completion list to Monaco");
                    await MonacoEditor.SendCompletionsAsync(e.RequestId, new System.Collections.Generic.List<MonacoCompletionItem>());
                    return;
                }

                // Convert Python completion data to Monaco format
                var monacoCompletions = new System.Collections.Generic.List<MonacoCompletionItem>();
                foreach (var completion in completions)
                {
                    var monacoItem = new MonacoCompletionItem
                    {
                        Label = completion.Text,
                        InsertText = completion.Text,
                        Detail = completion.Description?.ToString() ?? string.Empty,
                        Documentation = completion.Description?.ToString() ?? string.Empty,
                        SortText = completion.Text
                    };

                    // Map completion type to Monaco kind using actual CompletionType
                    if (completion is IronPythonCompletionData ironPythonCompletion)
                    {
                        // Map IronPythonCompletionData.CompletionType to Monaco CompletionItemKind
                        switch (ironPythonCompletion.CompletionTypeValue)
                        {
                            case IronPythonCompletionData.CompletionType.CLASS:
                                monacoItem.Kind = "Class";
                                break;
                            case IronPythonCompletionData.CompletionType.METHOD:
                                monacoItem.Kind = "Method";
                                break;
                            case IronPythonCompletionData.CompletionType.PROPERTY:
                                monacoItem.Kind = "Property";
                                break;
                            case IronPythonCompletionData.CompletionType.FIELD:
                                monacoItem.Kind = "Field";
                                break;
                            case IronPythonCompletionData.CompletionType.NAMESPACE:
                                monacoItem.Kind = "Module";
                                break;
                            case IronPythonCompletionData.CompletionType.ENUM:
                                monacoItem.Kind = "Enum";
                                break;
                            default:
                                monacoItem.Kind = "Text";
                                break;
                        }
                    }
                    else
                    {
                        monacoItem.Kind = "Text";
                    }

                    monacoCompletions.Add(monacoItem);
                }

                dynamoViewModel.Model.Logger.Log($"Sending {monacoCompletions.Count} completion items to Monaco");
                await MonacoEditor.SendCompletionsAsync(e.RequestId, monacoCompletions);
                dynamoViewModel.Model.Logger.Log($"=== Monaco Completion Request END ===");
            }
            catch (Exception ex)
            {
                dynamoViewModel.Model.Logger.Log($"ERROR: Failed to get Python completions: {ex.Message}");
                dynamoViewModel.Model.Logger.Log($"Stack trace: {ex.StackTrace}");
                await MonacoEditor.SendCompletionsAsync(e.RequestId, new System.Collections.Generic.List<MonacoCompletionItem>());
            }
        }

        private async void OnMonacoHoverRequested(object sender, MonacoHoverRequestEventArgs e)
        {
            try
            {
                if (completionProvider == null)
                {
                    await MonacoEditor.SendHoverAsync(e.RequestId, string.Empty);
                    return;
                }

                // Get completions to find the description for the hovered word
                var completions = completionProvider.GetCompletionData(e.Code, false);
                var matchingCompletion = completions?.FirstOrDefault(c => c.Text == e.Word);
                
                if (matchingCompletion != null)
                {
                    var description = matchingCompletion.Description?.ToString() ?? string.Empty;
                    await MonacoEditor.SendHoverAsync(e.RequestId, description);
                }
                else
                {
                    await MonacoEditor.SendHoverAsync(e.RequestId, string.Empty);
                }
            }
            catch (Exception ex)
            {
                dynamoViewModel.Model.Logger.Log($"Failed to get Python hover info: {ex.Message}");
                await MonacoEditor.SendHoverAsync(e.RequestId, string.Empty);
            }
        }

        private async void OnMonacoSignatureRequested(object sender, MonacoSignatureRequestEventArgs e)
        {
            try
            {
                if (completionProvider == null)
                {
                    await MonacoEditor.SendSignatureHelpAsync(e.RequestId, new System.Collections.Generic.List<string>());
                    return;
                }

                // Get completions to find function signatures
                var completions = completionProvider.GetCompletionData(e.Code, true); // Use expand=true to get more details
                var matchingCompletions = completions?.Where(c => c.Text == e.FunctionName).ToList();
                
                if (matchingCompletions != null && matchingCompletions.Any())
                {
                    var signatures = new System.Collections.Generic.List<string>();
                    foreach (var completion in matchingCompletions)
                    {
                        var description = completion.Description?.ToString() ?? string.Empty;
                        // Extract signature from description if available
                        if (!string.IsNullOrEmpty(description))
                        {
                            // Try to extract function signature from description
                            // Python completion descriptions often contain signatures
                            signatures.Add(description);
                        }
                        else
                        {
                            signatures.Add($"{e.FunctionName}(...)");
                        }
                    }
                    
                    await MonacoEditor.SendSignatureHelpAsync(e.RequestId, signatures, 0, 0);
                }
                else
                {
                    await MonacoEditor.SendSignatureHelpAsync(e.RequestId, new System.Collections.Generic.List<string>());
                }
            }
            catch (Exception ex)
            {
                dynamoViewModel.Model.Logger.Log($"Failed to get Python signature help: {ex.Message}");
                await MonacoEditor.SendSignatureHelpAsync(e.RequestId, new System.Collections.Generic.List<string>());
            }
        }


        private async Task ApplyMonacoPreferencesAsync()
        {
            if (MonacoEditor == null || !MonacoEditor.IsEditorReady)
                return;

            try
            {
                var fontSize = dynamoViewModel.PreferenceSettings.PythonScriptZoomScale * fontSizePreferencesSliderProportionValue;
                await MonacoEditor.SetFontSizeAsync((int)fontSize);
                await MonacoEditor.SetLineNumbersAsync(true);
            }
            catch (Exception)
            {
                // Silently handle errors during preference application
            }
        }

        private void OnMonacoEditorReady(object sender, EventArgs e)
        {
            _ = ApplyMonacoPreferencesAsync();
        }

        private void OnMonacoContentChanged(object sender, MonacoContentChangedEventArgs e)
        {
            nodeWasModified = e.Content != originalScript;
            IsSaved = !nodeWasModified;
        }

        private void UpdatePythonUpgradeBar()
        {
            var showForThisNode = NodeModel.ShowAutoUpgradedBar
                && dynamoViewModel.PreferenceSettings.ShowPythonAutoMigrationNotifications;

            PythonUpgradeBar.Visibility = showForThisNode ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DismissPythonUpgradeBar()
        {
            if (NodeModel.ShowAutoUpgradedBar && NodeModel.EngineName != PythonEngineManager.PythonNet3EngineName)
            {
                NodeModel.ShowAutoUpgradedBar = false;
                UpdatePythonUpgradeBar();
            }
        }

        private void PreferenceSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PreferenceSettings.ShowPythonAutoMigrationNotifications))
            {
                UpdatePythonUpgradeBar();
            }
        }

        /// <summary>
        /// Docks this window in the right side bar panel.
        /// </summary>
        /// <param name="name"></param>
        internal void DockWindow()
        {
            Uid = NodeModel.GUID.ToString();

            try
            {
                var dynamoView = Owner as DynamoView;
                var titleBar = FindName("TitleBar") as DockPanel;
                if (!useMonacoEditor)
                {
                    var editor = FindName("editText") as TextEditor;
                    if (editor != null)
                    {
                        editor.IsModified = !IsSaved;
                    }
                }

                dynamoView.DockWindowInSideBar(this, NodeModel, titleBar);

                Analytics.TrackEvent(
                               Actions.Dock,
                               Categories.PythonOperations, NodeModel.Name);

                Close();
            }
            catch (Exception ex)
            {
                dynamoViewModel.Model.Logger.Log("Failed to dock the Python Script editor.");
                dynamoViewModel.Model.Logger.Log(ex.Message);
                dynamoViewModel.Model.Logger.Log(ex.StackTrace);
            }
        }

        private void OnDockButtonClicked(object sender, RoutedEventArgs e)
        {
            DockWindow();
        }

        private void DynamoViewModel_PreferencesWindowChanged(object sender, EventArgs e)
        {
            var preferencesView = (Dynamo.Wpf.Views.PreferencesView)sender;
            preferencesView.PythonZoomScalingSlider.ValueChanged += PythonZoomScalingSlider_ValueChanged;
        }

        private void InstallFoldingManager()
        {
            editText.TextArea.IndentationStrategy = new PythonIndentationStrategy(editText);

            foldingManager = FoldingManager.Install(editText.TextArea);
            foldingStrategy = new TabFoldingStrategy();
            foldingStrategy.UpdateFoldings(foldingManager, editText.Document);

            editText.TextChanged += EditTextOnTextChanged;

            UpdateFoldings();
        }

        private void UpdateFoldings()
        {
            //if (!ShouldUpdate()) return;

            // Only update foldings when using AvalonEdit
            if (useMonacoEditor || foldingManager == null)
                return;

            // Since we cannot 'set' these values any other way, we need to change them on each update
            var margins = editText.TextArea.LeftMargins.OfType<FoldingMargin>();
            foreach (var margin in margins)
            {
                margin.FoldingMarkerBrush = new SolidColorBrush(Color.FromArgb(255, 153, 153, 153));
                margin.FoldingMarkerBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 53, 53, 53));
                margin.SelectedFoldingMarkerBrush = new SolidColorBrush(Color.FromArgb(255, 153, 153, 153));
                margin.SelectedFoldingMarkerBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 73, 73, 73));
            }

            foldingStrategy.UpdateFoldings(foldingManager, editText.Document);

            if (foldingManager.AllFoldings.Any())
            {
                foreach (var section in foldingManager.AllFoldings)
                {
                    // only set the title of the section once
                    if (section.Title != null) continue;

                    var title = section.TextContent.Split(new[] { '\r', '\n' }).FirstOrDefault() + "...";
                    section.Title = title;
                }
            }
        }

        private void EditTextOnTextChanged(object sender, EventArgs e)
        {
            // Mark the script for saving
            if (IsSaved) IsSaved = false;
            UpdateFoldings();
        }

        // Keeps track of Enter key use
        private bool IsEnterHit { get; set; }
        private bool ShouldUpdate()
        {
            if (!IsEnterHit) return false;
            var offset = editText.CaretOffset;
            var line = editText.Document.GetLineByNumber(editText.Document.GetLineByOffset(offset).LineNumber - 1);
            var text = editText.Document.GetText(line.Offset, line.Length);
            return text.EndsWith(":");
        }

        private void UpdateMigrationAssistantButtonEnabled()
        {
            var enable = CachedEngine == PythonEngineManager.IronPython2EngineName;

            MigrationAssistantButton.IsEnabled = enable;

            var tooltip = MigrationAssistantButton.ToolTip as System.Windows.Controls.ToolTip;
            var message = enable
                ? String.Format(
                    PythonNodeModels.Properties.Resources.PythonScriptEditorMigrationAssistantButtonTooltip,
                    PythonEngineManager.PythonNet3EngineName)
                : String.Format(
                    PythonNodeModels.Properties.Resources.PythonScriptEditorMigrationAssistantButtonDisabledTooltip,
                    PythonEngineManager.PythonNet3EngineName);
            tooltip.Content = message;
        }

        #region Text Zoom in Python Editor

        /// <summary>
        /// PreviewMouseWheel event handler to zoom in and out
        /// Additional check to make sure reacting to ctrl + mouse wheel
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void EditorBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            bool ctrl = Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control;
            if (ctrl)
            {
                this.UpdateFontSize(e.Delta > 0);
                e.Handled = true;
            }

            if (!useMonacoEditor)
            {
                int percentage = Convert.ToInt32(editText.FontSize / fontSizePreferencesSliderProportionValue);
                zoomScaleCacheValue = percentage;
                dynamoViewModel.PreferenceSettings.PythonScriptZoomScale = percentage;
            }
        }

        /// <summary>
        /// Function to increases/decreases font size in editor by a specific increment
        /// </summary>
        /// <param name="increase"></param>
        private async void UpdateFontSize(bool increase, double delta = 1.0)
        {
            if (delta == 0) return;
            
            if (useMonacoEditor && MonacoEditor != null)
            {
                var currentSize = dynamoViewModel.PreferenceSettings.PythonScriptZoomScale * fontSizePreferencesSliderProportionValue;
                if (increase)
                {
                    if (currentSize < FONT_MAX_SIZE)
                    {
                        double newSize = Math.Min(FONT_MAX_SIZE, currentSize + delta);
                        await MonacoEditor.SetFontSizeAsync((int)newSize);
                        zoomScaleCacheValue = (int)(newSize / fontSizePreferencesSliderProportionValue);
                        dynamoViewModel.PreferenceSettings.PythonScriptZoomScale = zoomScaleCacheValue;
                    }
                }
                else
                {
                    if (currentSize > FONT_MIN_SIZE)
                    {
                        double newSize = Math.Max(FONT_MIN_SIZE, currentSize - delta);
                        await MonacoEditor.SetFontSizeAsync((int)newSize);
                        zoomScaleCacheValue = (int)(newSize / fontSizePreferencesSliderProportionValue);
                        dynamoViewModel.PreferenceSettings.PythonScriptZoomScale = zoomScaleCacheValue;
                    }
                }
            }
            else
            {
                double currentSize = editText.FontSize;

                if (increase)
                {
                    if (currentSize < FONT_MAX_SIZE)
                    {
                        double newSize = Math.Min(FONT_MAX_SIZE, currentSize + delta);
                        editText.FontSize = newSize;
                    }
                }
                else
                {
                    if (currentSize > FONT_MIN_SIZE)
                    {
                        double newSize = Math.Max(FONT_MIN_SIZE, currentSize - delta);
                        editText.FontSize = newSize;
                    }
                }
            }
        }

        #endregion

        #region Autocomplete Event Handlers

        private void UpdateAvailableEngines(object sender = null, NotifyCollectionChangedEventArgs e = null)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (var item in e.NewItems)
                {
                    if (!AvailableEngines.Contains((string) item))
                    {
                        AvailableEngines.Add((string) item);
                    }
                }
            }
        }

        private void OnTextAreaTextEntering(object sender, TextCompositionEventArgs e)
        {
            try
            {
                if (e.Text.Length > 0 && completionWindow != null)
                {
                    if (!char.IsLetterOrDigit(e.Text[0]))
                        completionWindow.CompletionList.RequestInsertion(e);
                }
            }
            catch (Exception ex)
            {
                dynamoViewModel.Model.Logger.Log("Failed to perform python autocomplete with exception:");
                dynamoViewModel.Model.Logger.Log(ex.Message);
                dynamoViewModel.Model.Logger.Log(ex.StackTrace);
            }
        }

        private void OnTextAreaTextEntered(object sender, TextCompositionEventArgs e)
        {
            try
            {
                if (e.Text == ".")
                {
                    var subString = editText.Text.Substring(0, editText.CaretOffset);
                    var completions = completionProvider.GetCompletionData(subString, false);

                    if (completions == null || completions.Length == 0)
                    {
                        return;
                    }

                    completionWindow = new CompletionWindow(editText.TextArea);
                    var data = completionWindow.CompletionList.CompletionData;

                    foreach (var completion in completions)
                        data.Add(completion);

                    completionWindow.Show();
                    completionWindow.Closed += delegate { completionWindow = null; };
                }
            }
            catch (Exception ex)
            {
                dynamoViewModel.Model.Logger.Log("Failed to perform python autocomplete with exception:");
                dynamoViewModel.Model.Logger.Log(ex.Message);
                dynamoViewModel.Model.Logger.Log(ex.StackTrace);
            }
        }

        #endregion

        #region Private Event Handlers

        private void OnNodeModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PythonNode.EngineName))
            {
                if (CachedEngine != NodeModel.EngineName)
                {
                    CachedEngine = NodeModel.EngineName;
                    EngineSelectorComboBox.SelectedItem = CachedEngine;
                }
            }
        }

        private async void OnNodeModelCodeMigrated(object sender, PythonCodeMigrationEventArgs e)
        {
            originalScript = e.OldCode;
            await SetEditorTextAsync(e.NewCode);
            if (CachedEngine != PythonEngineManager.PythonNet3EngineName)
            {
                CachedEngine = PythonEngineManager.PythonNet3EngineName;
                EngineSelectorComboBox.SelectedItem = PythonEngineManager.PythonNet3EngineName;
            }
        }

        private async void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            await SaveScriptAsync();
        }

        private async Task SaveScriptAsync()
        {
            var scriptText = await GetEditorTextAsync();
            originalScript = scriptText;
            NodeModel.EngineName = CachedEngine;
            UpdateScript(scriptText);
            Analytics.TrackEvent(
                Dynamo.Logging.Actions.Save,
                Dynamo.Logging.Categories.PythonOperations);
            IsSaved = true;
            DismissPythonUpgradeBar();
        }

        private async void OnRevertClicked(object sender, RoutedEventArgs e)
        {
            if (nodeWasModified)
            {
                await SetEditorTextAsync(originalScript);
                CachedEngine = NodeModel.EngineName;
                EngineSelectorComboBox.SelectedItem = CachedEngine;
                UpdateScript(originalScript);
            }
        }

        private void UpdateScript(string scriptText)
        {
            var command = new DynamoModel.UpdateModelValueCommand(
                boundWorkspaceId, boundNodeId, propertyName, scriptText);

            dynamoViewModel.ExecuteCommand(command);
            this.Focus();
            nodeWasModified = true;
            IsSaved = true;
            NodeModel.OnNodeModified();
        }

        private async void OnRunClicked(object sender, RoutedEventArgs e)
        {
            NodeModel.EngineName = CachedEngine;
            var scriptText = await GetEditorTextAsync();
            UpdateScript(scriptText);
            if (dynamoViewModel.HomeSpace.RunSettings.RunType != RunType.Automatic)
            {
                dynamoViewModel.HomeSpace.Run();
            }
            DismissPythonUpgradeBar();

            Analytics.TrackEvent(
                Dynamo.Logging.Actions.Run,
                Dynamo.Logging.Categories.PythonOperations);
        }

        private async void OnMigrationAssistantClicked(object sender, RoutedEventArgs e)
        {
            if (NodeModel == null)
                throw new NullReferenceException(nameof(NodeModel));

            var scriptText = await GetEditorTextAsync();
            UpdateScript(scriptText);
            Analytics.TrackEvent(
                Dynamo.Logging.Actions.Migration,
                Dynamo.Logging.Categories.PythonOperations);
            NodeModel.RequestCodeMigration(e);
        }
        private async void OnConvertTabsToSpacesClicked(object sender, RoutedEventArgs e)
        {
            if (NodeModel == null)
                throw new NullReferenceException(nameof(NodeModel));

            if (useMonacoEditor && MonacoEditor != null)
            {
                var content = await MonacoEditor.GetContentAsync();
                if (!String.IsNullOrEmpty(content))
                {
                    var convertedText = PythonIndentationStrategy.ConvertTabsToSpaces(content);
                    await MonacoEditor.SetContentAsync(convertedText);
                }
            }
            else if (editText.Document != null && !String.IsNullOrEmpty(editText.Document.Text))
            {
                var convertedText = PythonIndentationStrategy.ConvertTabsToSpaces(editText.Document.Text);
                editText.Document.Text = convertedText;
            }
        }

        private void OnMoreInfoClicked(object sender, RoutedEventArgs e)
        {
            dynamoViewModel.OpenDocumentationLinkCommand.Execute(new OpenDocumentationLinkEventArgs(
                new Uri(PythonNodeModels.Properties.Resources.PythonMigrationWarningUriString, UriKind.Relative)));
        }

        private void OnEngineChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var enginecomboBox = sender as ComboBox;
            if (enginecomboBox != null && enginecomboBox.Name.Equals("EngineSelectorComboBox"))
            {
                CachedEngine = enginecomboBox.SelectedItem.ToString();
            }

            if (CachedEngine != NodeModel.EngineName)
            {
                nodeWasModified = true;
                // Cover what switch did user make. Only track when the new engine option is different with the previous one.
                Analytics.TrackEvent(
                    Actions.Switch,
                    Categories.PythonOperations,
                    CachedEngine);
            }

            editText.Options.ConvertTabsToSpaces = CachedEngine != PythonEngineManager.IronPython2EngineName;
            UpdateMigrationAssistantButtonEnabled();
        }

        private void OnScriptEditorWindowClosed(object sender, EventArgs e)
        {
            // When the script editor is docked, we don't want to dispose the editor functionality.
            // Dispose it only when the window is closed.
            if (!dynamoViewModel.DockedNodeWindows.Contains(Uid))
            {
                completionProvider?.Dispose();
                if (MonacoEditor != null)
                {
                    MonacoEditor.ContentChanged -= OnMonacoContentChanged;
                    MonacoEditor.EditorReady -= OnMonacoEditorReady;
                    MonacoEditor.CompletionRequested -= OnMonacoCompletionRequested;
                    MonacoEditor.HoverRequested -= OnMonacoHoverRequested;
                    MonacoEditor.SignatureRequested -= OnMonacoSignatureRequested;
                    MonacoEditor.Dispose();
                    MonacoEditor = null;
                }
                NodeModel.CodeMigrated -= OnNodeModelCodeMigrated;
                NodeModel.UserScriptWarned -= WarnUserScript;
                NodeModel.PropertyChanged -= OnNodeModelPropertyChanged;
                this.Closed -= OnScriptEditorWindowClosed;
                PythonEngineManager.Instance.AvailableEngines.CollectionChanged -= UpdateAvailableEngines;
                dynamoViewModel.PreferenceSettings.PropertyChanged -= PreferenceSettings_PropertyChanged;

                Analytics.TrackEvent(
                    Dynamo.Logging.Actions.Close,
                    Dynamo.Logging.Categories.PythonOperations);

                if (!useMonacoEditor)
                {
                    editText.TextChanged -= EditTextOnTextChanged;
                    if (foldingManager != null)
                    {
                        FoldingManager.Uninstall(foldingManager);
                        foldingManager = null;
                    }
                }
            }
        }

        private async void OnUndoClicked(object sender, RoutedEventArgs e)
        {
            if (useMonacoEditor && MonacoEditor != null)
            {
                await MonacoEditor.UndoAsync();
            }
            else
            {
                if (!editText.CanUndo) return;
                editText.Undo();
            }
            e.Handled = true;
        }

        private async void OnRedoClicked(object sender, RoutedEventArgs e)
        {
            if (useMonacoEditor && MonacoEditor != null)
            {
                await MonacoEditor.RedoAsync();
            }
            else
            {
                if (!editText.CanRedo) return;
                editText.Redo();
            }
            e.Handled = true;
        }

        private void OnZoomInClicked(object sender, RoutedEventArgs e)
        {
            UpdateFontSize(true);
            e.Handled = true;
        }

        private void OnZoomOutClicked(object sender, RoutedEventArgs e)
        {
            UpdateFontSize(false);
            e.Handled = true;
        }

        private void OnPythonAutoUpgradedBarClose(object sender, RoutedEventArgs e)
        {
            PythonUpgradeBar.Visibility = Visibility.Collapsed;
            NodeModel.ShowAutoUpgradedBar = false;
        }

        #endregion

        #region Navigation Controls

        private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_OnClick(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.Button).Name.Equals("MaximizeButton"))
            {
                this.WindowState = WindowState.Maximized;
                ToggleButtons(true);
            }
            else
            {
                this.WindowState = WindowState.Normal;
                ToggleButtons(false);
            }
        }

        /// <summary>
        /// Toggles between the Maximize and Normalize buttons on the window
        /// </summary>
        /// <param name="toggle"></param>
        private void ToggleButtons(bool toggle)
        {
            if (toggle)
            {
                this.MaximizeButton.Visibility = Visibility.Collapsed;
                this.NormalizeButton.Visibility = Visibility.Visible;
            }
            else
            {
                this.MaximizeButton.Visibility = Visibility.Visible;
                this.NormalizeButton.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Lets the user drag this window around with their left mouse button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UIElement_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            DragMove();
        }

        // Handles Close button 'X' 
        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            CloseWithWarning();
        }

        // Handles Close button on the Warning bar
        private void CloseWarningBarButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var senderContext = (sender as Button)?.DataContext;

                //At this point the user choses to close the window without saving the script, so mark the script as saved.
                IsSaved = true;

                if (this.Equals(senderContext))
                {
                    Close();
                }
                else // Close the right side extension tab if the close button is clicked on the docked editor. 
                {
                    var dynamoView = Owner as DynamoView;
                    TabItem tabItem = dynamoViewModel.SideBarTabItems.OfType<TabItem>().SingleOrDefault(n => n.Uid.ToString() == NodeModel.GUID.ToString());
                    if (tabItem != null)
                    {
                        dynamoView.CloseRightSideBarTab(tabItem);
                        dynamoViewModel.DockedNodeWindows.Remove(tabItem.Uid);
                    }
                }
            }
            catch (Exception ex)
            {
                dynamoViewModel.Model.Logger.Log("Failed to close the Python editor.");
                dynamoViewModel.Model.Logger.Log(ex.Message);
                dynamoViewModel.Model.Logger.Log(ex.StackTrace);
            }
        }

        // ESC Button pressed triggers Window close        
        private void OnCloseExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            e.Handled = true;
            CloseWithWarning();
        }

        // Close the script window if it was saved, otherwise issue a warning
        private void CloseWithWarning()
        {
            if (!IsSaved) WarnUserScript();
            else this.Close();
        }

        // Show the warning bar, hide the save button bar
        private void WarnUserScript()
        {
            this.editText.IsEnabled = false;
            this.SaveScriptChangesButton.IsEnabled = false;
            this.RevertScriptChangesButton.IsEnabled = false;
            this.UndoButton.IsEnabled = false;
            this.RedoButton.IsEnabled = false;
            this.ZoomInButton.IsEnabled = false;
            this.ZoomOutButton.IsEnabled = false;
            this.EngineSelectorComboBox.IsEnabled = false;
            this.MigrationAssistantButton.IsEnabled = false;
            this.ConvertTabsToSpacesButton.IsEnabled = false;
            this.MoreInfoButton.IsEnabled = false;
            this.SaveButtonBar.Visibility = Visibility.Collapsed;
            this.UnsavedChangesStatusBar.Visibility = Visibility.Visible;
        }

        // Don't change anything, just flip back the controls
        private void ResumeButton_OnClick(object sender, RoutedEventArgs e)
        {
            this.editText.IsEnabled = true;
            this.SaveScriptChangesButton.IsEnabled = true;
            this.RevertScriptChangesButton.IsEnabled = true;
            this.UndoButton.IsEnabled = true;
            this.RedoButton.IsEnabled = true;
            this.ZoomInButton.IsEnabled = true;
            this.ZoomOutButton.IsEnabled = true;
            this.EngineSelectorComboBox.IsEnabled = true;
            this.ConvertTabsToSpacesButton.IsEnabled = true;
            this.MoreInfoButton.IsEnabled = true;
            this.SaveButtonBar.Visibility = Visibility.Visible;
            this.UnsavedChangesStatusBar.Visibility = Visibility.Collapsed;
        }

        // Updates the IsEnterHit value
        private void EditText_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                IsEnterHit = true;
            }
            else
            {
                IsEnterHit = false;
            }
        }
        #endregion
    }
}
