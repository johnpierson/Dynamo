using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Dynamo.Configuration;
using Dynamo.Engine;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Nodes.ZeroTouch;
using Dynamo.Interfaces;
using Dynamo.Models;
using Dynamo.Search;
using Dynamo.Search.SearchElements;
using Dynamo.UI;
using Dynamo.Utilities;
using Dynamo.Wpf.Services;
using Dynamo.Wpf.Utilities;
using Dynamo.Wpf.ViewModels;
using DynamoUtilities;

namespace Dynamo.ViewModels
{
    public partial class SearchViewModel : ViewModelBase
    {
        #region events

        public event EventHandler RequestFocusSearch;
        public virtual void OnRequestFocusSearch()
        {
            if (RequestFocusSearch != null)
                RequestFocusSearch(this, EventArgs.Empty);
        }

        public event EventHandler SearchTextChanged;

        /// <summary>
        /// Invokes the SearchTextChanged event handler and executes the SearchCommand
        /// </summary>
        public void OnSearchTextChanged(object sender, EventArgs e)
        {
            if (SearchTextChanged != null)
                SearchTextChanged(this, e);

            // If this is trigged from NodeAutoCompleteSearchViewModel, we don't want to execute this SearchCommand on all nodes.
            // The Search will later be triggered on a subset of filtered results.
            if (this is NodeAutoCompleteSearchViewModel is false)
            {
                SearchCommand?.Execute(null);
            }
        }

        #endregion

        #region Properties/Fields

        private readonly IconServices iconServices;

        /// <summary>
        /// Position, where canvas was clicked. 
        /// After node will be called, it will be created at the same place.
        /// </summary>
        public Point InCanvasSearchPosition; 

        /// <summary>
        ///     Indicates whether the node browser is visible or not
        /// </summary>
        private bool browserVisibility = true;
        public bool BrowserVisibility
        {
            get { return browserVisibility; }
            set { browserVisibility = value; RaisePropertyChanged("BrowserVisibility"); }
        }

        internal int searchDelayTimeout = 150;
        // Feature flags activated debouncer for the search UI.
        internal ActionDebouncer searchDebouncer = null;
        // Cancel token source used for the node search operations.
        internal CancellationTokenSource searchCancelToken;
        // Enable running Search on a thread pool thread.
        private bool enableSearchThreading;

        private string searchText = string.Empty;
        /// <summary>
        ///     SearchText property
        /// </summary>
        /// <value>
        ///     This is the core UI for Dynamo, primarily used for logging.
        /// </value>
        public string SearchText
        {
            get { return searchText; }
            set
            {
                searchText = value;

                RaisePropertyChanged("SearchText");
                RaisePropertyChanged("BrowserRootCategories");
                RaisePropertyChanged("CurrentMode");

                // The searchText is set multiple times before the control becomes visible and interactable.
                // To prevent any debounces from triggering at some unexpected point before or after the control
                // becomes visible, this flag is only set once the searchText value is set by the user
                // (unless it somehow gets set somewhere else)
                //
                // pinzart: The search text is set multiple times with an empty value. Seems sufficient to only use the debouncer
                // if we get a non-empty value.
                if (!string.IsNullOrEmpty(searchText) && searchDebouncer != null)
                {
                    searchDebouncer.Debounce(searchDelayTimeout, () => OnSearchTextChanged(this, EventArgs.Empty));
                }
                else
                {
                    // Make sure any previously scheduled debounces are cancelled
                    searchDebouncer?.Cancel();
                    OnSearchTextChanged(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        ///     SearchIconAlignment property
        /// </summary>
        /// <value>
        ///     This is used for alignment search icon and text.
        /// </value>
        private HorizontalAlignment searchIconAlignment;
        public HorizontalAlignment SearchIconAlignment
        {
            get { return searchIconAlignment; }
            set
            {
                searchIconAlignment = value;
                RaisePropertyChanged("SearchIconAlignment");
            }
        }

        /// <summary>
        ///     Visible property
        /// </summary>
        /// <value>
        ///     Indicates whether the View is visible or not
        /// </value>
        private bool visible;
        public bool Visible
        {
            get { return visible; }
            set
            {
                visible = value;
                RaisePropertyChanged("Visible");
            }
        }

        public enum ViewMode { LibraryView, LibrarySearchView };

        /// <summary>
        /// The property specifies which View is active now.
        /// </summary>
        public ViewMode CurrentMode
        {
            get
            {
                return string.IsNullOrEmpty(SearchText.Trim()) ? ViewMode.LibraryView :
                    ViewMode.LibrarySearchView;
            }
        }

        /// <summary>
        ///  The property specifies which layout(detailed or compact) is used in search view.
        /// </summary>
        public bool IsDetailedMode
        {
            get
            {
                if (dynamoViewModel.Model.PreferenceSettings != null)
                {
                    return dynamoViewModel.Model.PreferenceSettings.ShowDetailedLayout;
                }
                else
                {
                    return true;
                }
            }
            set
            {
                if (dynamoViewModel.Model.PreferenceSettings != null
                    && dynamoViewModel.Model.PreferenceSettings.ShowDetailedLayout != value)
                {
                    dynamoViewModel.Model.PreferenceSettings.ShowDetailedLayout = value;
                    RaisePropertyChanged("IsDetailedMode");
                }
            }
        }

        /// <summary>
        ///     Items that were found during search.
        /// </summary>
        private List<NodeSearchElementViewModel> searchResults;

        private IEnumerable<NodeSearchElementViewModel> filteredResults;
        /// <summary>
        /// Filtered search results.
        /// </summary>
        public IEnumerable<NodeSearchElementViewModel> FilteredResults
        {
            get
            {
                return filteredResults;
            }
            set
            {
                filteredResults = ToggleSelect(value);
                RaisePropertyChanged(nameof(FilteredResults));
            }
        }

        private IEnumerable<NodeSearchElementViewModel> filteredHighConfidenceResults;
        /// <summary>
        /// Filtered high confidence search results.
        /// </summary>
        internal IEnumerable<NodeSearchElementViewModel> FilteredHighConfidenceResults
        {
            get
            {
                return filteredHighConfidenceResults;
            }
            set
            {
                filteredHighConfidenceResults = ToggleSelect(value);
                RaisePropertyChanged(nameof(FilteredHighConfidenceResults));
            }
        }


        private IEnumerable<NodeSearchElementViewModel> filteredLowConfidenceResults;
        /// <summary>
        /// Filtered low confidence search results.
        /// </summary>
        internal IEnumerable<NodeSearchElementViewModel> FilteredLowConfidenceResults
        {
            get
            {
                return filteredLowConfidenceResults;
            }
            set
            {
                filteredLowConfidenceResults = ToggleSelect(value);
                RaisePropertyChanged(nameof(FilteredLowConfidenceResults));
            }
        }

        /// <summary>
        /// Filters search items, if category was selected.
        /// </summary>
        internal void Filter()
        {
            var allowedCategories = SearchCategories.Where(cat => cat.IsSelected);
            FilteredResults = searchResults.Where(x => allowedCategories
                                                                       .Select(cat => cat.Name)
                                                                       .Contains(x.Category));

            // Report selected categories to instrumentation
            StringBuilder strBuilder = new StringBuilder();
            foreach (var category in SearchCategories)
            {
                strBuilder.Append(category.Name);
                strBuilder.Append(" : ");
                if (category.IsSelected)
                {
                    strBuilder.Append("Selected");
                }
                else
                {
                    strBuilder.Append("Unselected");
                }
                strBuilder.Append(", ");
            }
        }

        /// <summary>
        /// Unselects all items and selects the first one.
        /// </summary>
        internal IEnumerable<NodeSearchElementViewModel> ToggleSelect(IEnumerable<NodeSearchElementViewModel> items)
        {
            if (!items.Any())
            {
                return items;
            }

            // Unselect all.
            items.Skip(1).ToList().ForEach(x => x.IsSelected = false);
            // Select first.
            items.First().IsSelected = true;

            return items;
        }

        /// <summary>
        /// Returns true, if it was found at least one item. Otherwise it returns false.
        /// </summary>
        public bool IsAnySearchResult
        {
            get
            {
                return searchResults.Any();
            }
        }

        private IEnumerable<SearchCategory> searchCategories;
        /// <summary>
        /// Categories that were found after search. Used to filter search results.
        /// </summary>
        public IEnumerable<SearchCategory> SearchCategories
        {
            get
            {
                return searchCategories;
            }
            private set
            {
                searchCategories = value.OrderBy(category => category.Name);
                RaisePropertyChanged("SearchCategories");
            }
        }

        private bool searchScrollBarVisibility = true;
        public bool SearchScrollBarVisibility
        {
            get { return searchScrollBarVisibility; }
            set { searchScrollBarVisibility = value; RaisePropertyChanged("SearchScrollBarVisibility"); }
        }

        public Typeface RegularTypeface { get; private set; }

        public ObservableCollection<NodeCategoryViewModel> LibraryRootCategories
        {
            get { return libraryRoot.SubCategories; }
        }

        private readonly NodeCategoryViewModel libraryRoot = new NodeCategoryViewModel("");

        public ObservableCollection<NodeCategoryViewModel> BrowserRootCategories
        {
            get { return LibraryRootCategories; }
        }

        /// <summary>
        /// To get view model for a node based on its name
        /// </summary>
        /// <param name="nodeName"></param>
        /// <returns></returns>
        public NodeSearchElementViewModel FindViewModelForNode(string nodeName)
        {
            var result = Model.Entries.Where(e => {
                if (e.CreationName.Equals(nodeName))
                {
                    return true;
                }
                return false;
            });

            if (!result.Any())
            {
                return null;
            }

            return MakeNodeSearchElementVM(result.ElementAt(0));
        }

        public NodeSearchModel Model { get; private set; }
        internal readonly DynamoViewModel dynamoViewModel;

        // Lucene search utility to perform indexing operations.
        private LuceneSearchUtility LuceneUtility
        {
            get
            {
                return LuceneSearch.LuceneUtilityNodeSearch;
            }
        }

        /// <summary>
        /// Class name, that has been clicked in library search view.
        /// </summary>
        internal string ClassNameToBeOpened;
        #endregion

        #region Initialization

        internal SearchViewModel(DynamoViewModel dynamoViewModel)
        {
            Model = dynamoViewModel.Model.SearchModel;
            this.dynamoViewModel = dynamoViewModel;

            IPathManager pathManager = null;
            if (dynamoViewModel != null && (dynamoViewModel.Model != null))
                pathManager = dynamoViewModel.Model.PathManager;

            iconServices = new IconServices(pathManager);

            DynamoFeatureFlagsManager.FlagsRetrieved += TryInitializeSearchFlags;

            InitializeCore();
        }

        private void TryInitializeSearchFlags()
        {
            if (DynamoModel.FeatureFlags?.CheckFeatureFlag("searchbar_debounce", false) ?? false)
            {
                searchDebouncer ??= new ActionDebouncer(dynamoViewModel?.Model?.Logger);
            }

            if (DynamoModel.FeatureFlags?.CheckFeatureFlag("searchbar_separate_thread", false) ?? false)
            {
                enableSearchThreading = true;
            }
        }

        // Just for tests. Please refer to LibraryTests.cs
        internal SearchViewModel(NodeSearchModel model)
        {
            Model = model;
            InitializeCore();
        }

        internal SearchViewModel(NodeSearchModel model, DynamoViewModel dynamoViewModel)
        {
            Model = model;
            InitializeCore();
        }

        /// <summary>
        /// Dispose function
        /// </summary>
        public override void Dispose()
        {
            foreach (var cate in LibraryRootCategories)
            {
                cate.DisposeTree();
            }
            foreach (var cate in BrowserRootCategories)
            {
                cate.DisposeTree();
            }
            Model.EntryAdded -= AddEntry;
            Model.EntryUpdated -= UpdateEntry;
            Model.EntryRemoved -= RemoveEntry;

            searchDebouncer?.Dispose();
            DynamoFeatureFlagsManager.FlagsRetrieved -= TryInitializeSearchFlags;

            base.Dispose();
        }

        private void InitializeCore()
        {
            searchResults = new List<NodeSearchElementViewModel>();
            filteredResults = new List<NodeSearchElementViewModel>();
            searchCategories = new List<SearchCategory>();

            Visible = false;
            searchText = "";

            var fontFamily = new FontFamily(SharedDictionaryManager.DynamoModernDictionaryUri, "../../Fonts/#Open Sans");
            RegularTypeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal,
                FontStretches.Normal);

            searchIconAlignment = System.Windows.HorizontalAlignment.Left;

            // When Library changes, sync up
            Model.EntryAdded += AddEntry;
            Model.EntryUpdated += UpdateEntry;
            Model.EntryRemoved += RemoveEntry;

            LibraryRootCategories.AddRange(CategorizeEntries(Model.Entries, false));

            DefineFullCategoryNames(LibraryRootCategories, "");
            InsertClassesIntoTree(LibraryRootCategories);

            // If feature flags are already cached, try to initialize the debouncer
            TryInitializeSearchFlags();
        }

        private void AddEntry(NodeSearchElement entry)
        {
            InsertEntry(MakeNodeSearchElementVM(entry), entry.Categories);
            RaisePropertyChanged("BrowserRootCategories");
        }

        private IEnumerable<RootNodeCategoryViewModel> CategorizeEntries(IEnumerable<NodeSearchElement> entries, bool expanded)
        {
            var tempRoot = entries.GroupByRecursive<NodeSearchElement, string, NodeCategoryViewModel>(
                    element => element.Categories,
                    (name, subs, es) =>
                    {
                        var category =
                            new NodeCategoryViewModel(name, es.OrderBy(en => en.Name).Select(MakeNodeSearchElementVM), subs);
                        category.IsExpanded = expanded;
                        category.RequestBitmapSource += SearchViewModelRequestBitmapSource;
                        category.RequestReturnFocusToSearch += OnRequestFocusSearch;
                        return category;
                    }, "");

            var result = tempRoot.SubCategories.Select(cat =>
            {
                var rootCat = new RootNodeCategoryViewModel(cat.Name, cat.Entries, cat.SubCategories)
                {
                    IsExpanded = expanded
                };

                rootCat.RequestReturnFocusToSearch += OnRequestFocusSearch;
                // Since all the root categories will be new RootNodeCategoryViewModel objects,
                // we should dispose the old ones. Since they are still watching for subcategories'
                // property changes, they will never be garbage collected.
                cat.Dispose();
                return rootCat;
            });

            tempRoot.Dispose();
            return result.OrderBy(cat => cat.Name);
        }

        private static void InsertClassesIntoTree(ObservableCollection<NodeCategoryViewModel> tree)
        {
            foreach (var item in tree)
            {
                var classes = item.SubCategories.Where(cat => cat.IsClassButton).ToList();
                foreach (var item2 in classes)
                    item.SubCategories.Remove(item2);

                InsertClassesIntoTree(item.SubCategories);

                if (classes.Count == 0)
                    continue;

                var container = new ClassesNodeCategoryViewModel(item);
                container.SubCategories.AddRange(classes);

                item.SubCategories.Insert(0, container);
            }
        }

        private static void DefineFullCategoryNames(ObservableCollection<NodeCategoryViewModel> tree, string path)
        {
            foreach (var item in tree)
            {
                item.FullCategoryName = MakeFullyQualifiedName(path, item.Name);
                if (!item.SubCategories.Any())
                    item.Assembly = (item.Items[0] as NodeSearchElementViewModel).Assembly;

                DefineFullCategoryNames(item.SubCategories, item.FullCategoryName);
            }
        }

        internal void UpdateEntry(NodeSearchElement entry)
        {
            //look for the viewModel which should own this nodeSearchElement entry.
            var rootNode = libraryRoot;
            foreach (var categoryName in entry.Categories)
            {
                var tempNode = rootNode.SubCategories.FirstOrDefault(item => item.Name == categoryName);
                // Root node can be null, if there is classes-viewmodel between updated entry and current category.
                if (tempNode == null)
                {
                    // Get classes.
                    var classes = rootNode.SubCategories.FirstOrDefault();
                    // Search in classes.
                    tempNode = classes.SubCategories.FirstOrDefault(item => item.Name == categoryName);
                }

                rootNode = tempNode;
            }
            var entryVM = rootNode.Entries.FirstOrDefault(foundEntryVM => foundEntryVM.Name == entry.Name);
            entryVM.Model = entry;
        }

        internal void RemoveEntry(NodeSearchElement entry)
        {            
            var branch = GetTreeBranchToNode(libraryRoot, entry);
            if (!branch.Any())
                return;
            var treeStack = new Stack<NodeCategoryViewModel>(branch.Reverse());

            var target = treeStack.Pop();
          
            var location = target.Entries.Select((e, i) => new { e.Model, i })
                .FirstOrDefault(x => entry == x.Model);
            if (location == null)
                return;
            target.Entries.RemoveAt(location.i);
           
            while (!target.Items.Any() && treeStack.Any())
            {
                var parent = treeStack.Pop();
                parent.SubCategories.Remove(target);
                parent.Items.Remove(target);

                // Check to see if all items under "parent" are removed, leaving behind only one 
                // entry that is "ClassInformationViewModel" (a class used to show ClassInformationView).
                // If that is the case, remove the "ClassInformationViewModel" at the same time.
                if (parent.Items.Count == 1 && parent.Items[0] is ClassInformationViewModel)
                    parent.Items.RemoveAt(0);
                target = parent;
            }

            // After removal of category "target" can become the class.
            // In this case we need to add target to existing classes contaiiner 
            // (ClassesNodeCategoryViewModel) or create new one.
            // For example we have a structure.
            //
            //                         Top
            //                          │
            //                       Sub1_1  
            //             ┌────────────┤       
            //          Sub2_1       Classes 
            //    ┌────────┤            │     
            // Classes     Member2   Sub2_2   
            //    │                     │     
            // Sub3_1                   Member3
            //    │                            
            //    Member1   
            // 
            // Let's remove "Member1". Before next code we have removed entry "Member1" and
            // categories "Sub3_1", "Classes". "Sub2_1" is "target" as soon as it has one item in
            // Items collection. Next code will deattach from "Sub1_1" and attach target to another
            // "Classes" category.
            // Structure should become.
            //
            //                         Top
            //                          │
            //                       Sub1_1  
            //                          │  
            //                       Classes 
            //               ┌──────────┤ 
            //            Sub2_1     Sub2_2   
            //               │          │     
            //               Member2    Member3    
            //
            if (treeStack.Any() && !target.SubCategories.Any())
            {
                var parent = treeStack.Pop();
                // Do not continue if parent is already in classes container.
                if (parent is ClassesNodeCategoryViewModel && parent.SubCategories.Contains(target))
                    return;

                // Do not continue as soon as our target is not class.
                if (target.SubCategories.Any())
                    return;

                if (!(parent.SubCategories[0] is ClassesNodeCategoryViewModel))
                    parent.SubCategories.Insert(0, new ClassesNodeCategoryViewModel(parent));

                if (!parent.SubCategories[0].SubCategories.Contains(target))
                {
                    // Reattach target from parent to classes container.
                    parent.SubCategories.Remove(target);
                    parent.SubCategories[0].SubCategories.Add(target);
                }
            }
        }

        private static IEnumerable<NodeCategoryViewModel> GetTreeBranchToNode(
            NodeCategoryViewModel rootNode, NodeSearchElement leafNode)
        {
            var nodesOnBranch = new Stack<NodeCategoryViewModel>();
            var nameStack = new Stack<string>(leafNode.Categories.Reverse());
            var target = rootNode;
            bool isCheckedForClassesCategory = false;
            while (nameStack.Any())
            {
                var next = nameStack.Pop();
                var categories = target.SubCategories;
                var newTarget = categories.FirstOrDefault(c => c.Name == next);
                if (newTarget == null)
                {
                    // The last entry in categories list can be a class name. When the desired class 
                    // cannot be located with "MyAssembly.MyNamespace.ClassCandidate" pattern, try 
                    // searching with "MyAssembly.MyNamespace.Classes.ClassCandidate" instead. This 
                    // is because a class always resides under a "ClassesNodeCategoryViewModel" node.
                    //
                    if (!isCheckedForClassesCategory && nameStack.Count == 0)
                    {
                        nameStack.Push(next);
                        nameStack.Push(Configurations.ClassesDefaultName);

                        isCheckedForClassesCategory = true;
                        continue;
                    }

                    return Enumerable.Empty<NodeCategoryViewModel>();
                }
                nodesOnBranch.Push(target);
                target = newTarget;
            }

            nodesOnBranch.Push(target);
            return nodesOnBranch;
        }

        /// <summary>
        /// Insert a new search element under the category.
        /// </summary>
        /// <param name="entry">This could represent a function of a given 
        /// class. For example, 'MyAssembly.MyNamespace.MyClass.Foo'.</param>
        /// <param name="categoryNames">A list of entries that make up the fully qualified
        /// class name that contains function 'Foo', e.g. 'MyAssembly.MyNamespace.MyClass'.
        /// </param>
        internal void InsertEntry(NodeSearchElementViewModel entry, IEnumerable<string> categoryNames)
        {
            var nameStack = new Stack<string>(categoryNames.Reverse());
            var target = libraryRoot;
            string fullyQualifiedCategoryName = "";
            ClassesNodeCategoryViewModel targetClass = null;
            while (nameStack.Any())
            {
                var next = nameStack.Pop();
                fullyQualifiedCategoryName = MakeFullyQualifiedName(fullyQualifiedCategoryName, next);

                var categories = target.SubCategories;
                NodeCategoryViewModel targetClassSuccessor = null;
                var newTarget = categories.FirstOrDefault(c =>
                {
                    // Each path has one class. We should find and save it.                    
                    if (c is ClassesNodeCategoryViewModel)
                    {
                        targetClass = c as ClassesNodeCategoryViewModel;
                        // As soon as ClassesNodeCategoryViewModel is found we should search 
                        // through all it classes and save result.
                        targetClassSuccessor = c.SubCategories.FirstOrDefault(c2 => c2.Name == next);
                        return targetClassSuccessor != null;
                    }

                    return c.Name == next;
                });
                if (newTarget == null)
                {
                    // For the first iteration, this would be 'MyAssembly', and the second iteration 'MyNamespace'.
                    var targetIsRoot = target == libraryRoot;
                    newTarget = targetIsRoot ? new RootNodeCategoryViewModel(next) : new NodeCategoryViewModel(next);
                    newTarget.FullCategoryName = fullyQualifiedCategoryName;
                    newTarget.Assembly = entry.Assembly;
                    // Situation when we to add only one new category and item as it child.
                    // New category should be added to existing ClassesNodeCategoryViewModel.
                    // Make notice: ClassesNodeCategoryViewModel is always first item in 
                    // all subcategories.
                    if (nameStack.Count == 0 && !target.IsClassButton &&
                        target.SubCategories[0] is ClassesNodeCategoryViewModel)
                    {
                        target.SubCategories[0].SubCategories.Add(newTarget);
                        AddEntryToExistingCategory(newTarget, entry);
                        return;
                    }

                    // We are here when target is the class. New category should be added
                    // as child of it. So class will turn into usual category.
                    // Here we are take class, remove it from ClassesNodeCategoryViewModel
                    // and attach to it parrent.
                    if (targetClass != null)
                    {
                        if (targetClass.SubCategories.Remove(target))
                            targetClass.Parent.SubCategories.Add(target);
                        // Delete empty classes container.
                        if (targetClass.IsClassButton)
                            targetClass.Parent.SubCategories.RemoveAt(0);

                        targetClass.Dispose();
                    }

                    // Situation when we need to add only one new category and item.
                    // Before adding of it we need create new ClassesNodeCategoryViewModel
                    // as soon as new category will be a class.
                    if (nameStack.Count == 0 && !targetIsRoot)
                    {
                        targetClass = new ClassesNodeCategoryViewModel(target);

                        target.SubCategories.Insert(0,targetClass);
                        target.SubCategories[0].SubCategories.Add(newTarget);
                        AddEntryToExistingCategory(newTarget, entry);
                        return;
                    }

                    target.InsertSubCategory(newTarget);

                    // Proceed to insert the new entry under 'newTarget' category with the remaining 
                    // name stack. In the first iteration this would have been 'MyNamespace.MyClass'.
                    InsertEntryIntoNewCategory(newTarget, entry, nameStack);
                    return;
                }
                // If we meet ClassesNodecategoryViewModel during the search of newTarget,
                // next newTarget is specified in targetClassSuccessor.
                if (targetClassSuccessor != null)
                    target = targetClassSuccessor;
                else
                    target = newTarget;
            }
            AddEntryToExistingCategory(target, entry);
        }

        private void InsertEntryIntoNewCategory(
            NodeCategoryViewModel category,
            NodeSearchElementViewModel entry,
            IEnumerable<string> categoryNames)
        {
            if (!categoryNames.Any())
            {
                AddEntryToExistingCategory(category, entry);
                return;
            }

            // With the example of 'MyAssembly.MyNamespace.MyClass.Foo', 'path' would have been 
            // set to 'MyAssembly' here. The Select statement below would store two entries into
            // 'newTargets' variable:
            // 
            //      NodeCategoryViewModel("MyAssembly.MyNamespace")
            //      NodeCategoryViewModel("MyAssembly.MyNamespace.MyClass")
            // 
            var path = category.FullCategoryName;
            var newTargets = categoryNames.Select(name =>
            {
                path = MakeFullyQualifiedName(path, name);

                var cat = new NodeCategoryViewModel(name);
                cat.FullCategoryName = path;
                cat.Assembly = entry.Assembly;
                return cat;
            }).ToList();

            // The last entry 'NodeCategoryViewModel' represents a class. For our example the 
            // entries in 'newTargets' are:
            // 
            //      NodeCategoryViewModel("MyAssembly.MyNamespace")
            //      NodeCategoryViewModel("MyAssembly.MyNamespace.MyClass")
            // 
            // Since all class entries are contained under a 'ClassesNodeCategoryViewModel', 
            // we need to create a new 'ClassesNodeCategoryViewModel' instance, and insert it 
            // right before the class entry itself to get the following list:
            // 
            //      NodeCategoryViewModel("MyAssembly.MyNamespace")
            //      ClassesNodeCategoryViewModel("Classes")
            //      NodeCategoryViewModel("MyAssembly.MyNamespace.MyClass")
            // 
            int indexToInsertClass = newTargets.Count - 1;
            var classParent = indexToInsertClass > 0 ? newTargets[indexToInsertClass - 1] : category;
            var newClass = new ClassesNodeCategoryViewModel(classParent);
            newTargets.Insert(indexToInsertClass, newClass);

            // Here, all the entries in 'newTargets' are added under 'MyAssembly' recursively,
            // resulting in the following hierarchical structure:
            // 
            //      NodeCategoryViewModel("MyAssembly")
            //          NodeCategoryViewModel("MyAssembly.MyNamespace")
            //              ClassesNodeCategoryViewModel("Classes")
            //                  NodeCategoryViewModel("MyAssembly.MyNamespace.MyClass")
            // 
            foreach (var newTarget in newTargets)
            {
                category.SubCategories.Add(newTarget);
                category = newTarget;
            }

            AddEntryToExistingCategory(category, entry);
        }

        private void AddEntryToExistingCategory(NodeCategoryViewModel category,
            NodeSearchElementViewModel entry)
        {
            category.RequestBitmapSource += SearchViewModelRequestBitmapSource; 
            // Check if the category exists already. 
            // ex : clockwork package. For clockwork 
            // package the category names in dyf is different from what we show it 
            // on the tree view. so when you click on the category to populate it 
            // triggers an update to category name. on the same instance when you uninstall
            // and install the clockwork package, the categories are named correctly but 
            // every install triggers an update that gives a duplicate entry. so check if the
            // entry is already added (specific to browse).
            if (category.Entries.All(x => x.FullName != entry.FullName))
            {
                category.Entries.Add(entry);
            }
        }

        internal void SearchViewModelRequestBitmapSource(IconRequestEventArgs e)
        {
            var warehouse = iconServices.GetForAssembly(e.IconAssembly, e.UseAdditionalResolutionPaths);
            ImageSource icon = null;
            if (warehouse != null)
                icon = warehouse.LoadIconInternal(e.IconFullPath);

            e.SetIcon(icon);
        }

        // Form a fully qualified name based on nested level of a "NodeCategoryViewModel" object.
        // For example, "Core.File.Directory" is the fully qualified name for "Directory".
        private static string MakeFullyQualifiedName(string path, string addition)
        {
            return string.IsNullOrEmpty(path) ? addition :
                path + Configurations.CategoryDelimiterString + addition;
        }

        #endregion

        #region Search

        /// <summary>
        ///     Performs a search using the internal SearcText as the query.
        /// </summary>
        internal void SearchAndUpdateResults()
        {
            searchResults.Clear();

            if (!String.IsNullOrEmpty(SearchText.Trim()))
            {
                SearchAndUpdateResults(SearchText);
            }

            RaisePropertyChanged("IsAnySearchResult");
        }

        internal Task SearchAndUpdateResultsTask(string query)
        {
            if (Visible != true)
                return Task.CompletedTask;

            // if the search query is empty, go back to the default treeview
            if (string.IsNullOrEmpty(query))
                return Task.CompletedTask;

            // A new search should cancel any existing searches.
            searchCancelToken?.Cancel();
            searchCancelToken?.Dispose();

            searchCancelToken = new();

            // The TaskScheduler.FromCurrentSynchronizationContext() exists only if there is a valid SyncronizationContex/
            // Calling this method from a non UI thread could have a null SyncronizationContex.Current,
            // so in that case we use the default TaskScheduler which uses the thread pool.
            var taskScheduler = SynchronizationContext.Current != null ? TaskScheduler.FromCurrentSynchronizationContext() : TaskScheduler.Default;

            // We run the searches on the thread pool to reduce the impact on the UI thread.
            return Task.Run(() =>
            {
                return Search(query, searchCancelToken.Token);

            }, searchCancelToken.Token).ContinueWith((t, o) =>
            {
                // This continuation will execute on the UI thread (forced by using FromCurrentSynchronizationContext())
                searchResults = new List<NodeSearchElementViewModel>(t.Result);

                FilteredResults = searchResults;
                UpdateSearchCategories();
            }, taskScheduler, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        /// <summary>
        ///     Performs a search and updates searchResults.
        /// </summary>
        /// <param name="query"> The search query </param>
        [Obsolete(@"This method will be removed in a future release. The internal search operation is done asyncronously, so when this method call exits, the search operation might not be done yet.
                    Please use the task based method SearchAndUpdateResultsTask instead.")]
        public void SearchAndUpdateResults(string query)
        {
            if (enableSearchThreading)
            {
                SearchAndUpdateResultsTask(query);
            }
            else
            {
                if (Visible != true)
                    return;

                // if the search query is empty, go back to the default treeview
                if (string.IsNullOrEmpty(query))
                    return;

                //Passing the second parameter as true will search using Lucene.NET
                var foundNodes = Search(query);
                searchResults = new List<NodeSearchElementViewModel>(foundNodes);

                FilteredResults = searchResults;
                UpdateSearchCategories();
            }
        }

        /// <summary>
        /// Select unique search categories, which are used in search UI.
        /// </summary>
        private void UpdateSearchCategories()
        {
            var uniqueCategoryNames = searchResults.Select(x => x.Category).Distinct();

            var categories = new List<SearchCategory>();
            foreach (var name in uniqueCategoryNames)
            {
                var searchCategory = new SearchCategory(name);
                searchCategory.PropertyChanged += IsSelectedChanged;
                categories.Add(searchCategory);
            }
            SearchCategories = categories;
        }

        /// <summary>
        /// When category is selected, search results should be updated and contain nodes from this category.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void IsSelectedChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "IsSelected")
            {
                return;
            }

            Filter();
        }

        /// <summary>
        ///     Performs a search using the given string as query and subset, if provided. Uses Lucene search.
        /// </summary>
        /// <returns> Returns a list with a maximum MaxNumSearchResults elements.</returns>
        /// <param name="search"> The search query </param>
        /// <param name="ctk">A cancellation token for this operation.</param>
        internal IEnumerable<NodeSearchElementViewModel> Search(string search, CancellationToken ctk = default)
        {
            if (LuceneUtility != null)
            {
                var searchElements = Model.Search(search, LuceneUtility, ctk);
                if (searchElements != null)
                {
                    return searchElements.Select(MakeNodeSearchElementVM);
                }
            }
            return null;
        }

        /// <summary>
        /// To get view model for a node based on its name and category
        /// </summary>
        /// <param name="nodeName">Name of the node</param>
        /// <param name="nodeCategory">Full Category of the node</param>
        /// <param name="parameters">Node input parameters</param>
        /// <returns></returns>
        internal NodeSearchElementViewModel FindViewModelForNodeNameAndCategory(string nodeName, string nodeCategory, string parameters)
        {
            var result = Model.Entries.Where(e => {
                if (e.Name.Equals(nodeName) && e.FullCategoryName.Equals(nodeCategory))
                {
                    //When the node info was indexed if Parameters was null we added an empty space (null cannot be indexed)
                    //Then in this case when searching if e.Parameters is null we need to check against empty space
                    if (e.Parameters == null)
                        return string.IsNullOrEmpty(parameters);
                    //Parameters contain a value so we need to compare against the value indexed
                    else
                        return e.Parameters.Equals(parameters);
                }
                return false;
            });

            if (!result.Any())
            {
                return null;
            }

            return MakeNodeSearchElementVM(result.ElementAt(0));
        }

        private static IEnumerable<NodeSearchElementViewModel> GetVisibleSearchResults(NodeCategoryViewModel category)
        {
            foreach (var item in category.Items)
            {
                var sub = item as NodeCategoryViewModel;
                if (sub != null)
                {
                    foreach (var visible in GetVisibleSearchResults(sub))
                        yield return visible;
                }
                else
                    yield return (NodeSearchElementViewModel)item;
            }
        }

        internal NodeSearchElementViewModel MakeNodeSearchElementVM(NodeSearchElement entry)
        {
            var element = entry as CustomNodeSearchElement;
            var elementVM = element != null
                ? new CustomNodeSearchElementViewModel(element, this)
                : new NodeSearchElementViewModel(entry, this);
            //TODO lookout for leak.
            elementVM.RequestBitmapSource += SearchViewModelRequestBitmapSource;
            return elementVM;
        }

        private static NodeCategoryViewModel GetCategoryViewModel(NodeCategoryViewModel rootCategory,
            IEnumerable<string> categories)
        {
            var nameStack = new Stack<string>(categories.Reverse());
            NodeCategoryViewModel target = rootCategory;
            NodeCategoryViewModel newTarget = null;
            bool isCheckedForClassesCategory = false;
            while (nameStack.Any())
            {
                var currentCategory = nameStack.Pop();
                newTarget = target.SubCategories.FirstOrDefault(c => c.Name == currentCategory);
                if (newTarget == null)
                {
                    if (!isCheckedForClassesCategory && !target.IsClassButton &&
                        target.SubCategories[0] is ClassesNodeCategoryViewModel)
                    {
                        isCheckedForClassesCategory = true;
                        nameStack.Push(currentCategory);
                        nameStack.Push(Configurations.ClassesDefaultName);
                        continue;
                    }

                    return null;
                }

                target = newTarget;
            }

            return target;
        }

        private static string MakeShortCategoryString(string fullCategoryName)
        {
            var catName = fullCategoryName.Replace(".", " > ");

            if (catName.Length <= 50)
                return catName;

            // if the category name is too long, we strip off the interior categories
            var s = catName.Split('>').Select(x => x.Trim()).ToList();
            if (s.Count() <= 4)
                return catName;

            s = new List<string>
            {
                s[0],
                "...",
                s[s.Count - 3],
                s[s.Count - 2],
                s[s.Count - 1]
            };
            catName = string.Join(" > ", s);

            return catName;
        }

        #endregion

        #region Key navigation

        public enum Direction
        {
            Down, Up
        }

        /// <summary>
        /// Executes selected item in search UI.
        /// </summary>
        public void ExecuteSelectedItem()
        {
            var selected = FilteredResults.FirstOrDefault(item => item.IsSelected);

            if (selected != null)
            {
                selected.ClickedCommand.Execute(null);
            }
        }

        /// <summary>
        /// When down key is pressed, selected element should move forward.
        /// When up key is pressed, selected element should move back.
        /// </summary>
        public void MoveSelection(Direction direction)
        {
            var oldItem = FilteredResults.FirstOrDefault(item => item.IsSelected);
            if (oldItem == null) return;

            int newItemIndex = FilteredResults.IndexOf(oldItem);
            if ((newItemIndex <= 0 && direction == Direction.Up) ||
                (newItemIndex >= FilteredResults.Count() - 1 && direction == Direction.Down)) return;

            if (direction == Direction.Down)
            {
                newItemIndex++;
            }
            else
            {
                newItemIndex--;
            }

            oldItem.IsSelected = false;
            var newItem = FilteredResults.ElementAt(newItemIndex);
            newItem.IsSelected = true;
        }

        #endregion

        #region Search field manipulation

        /// <summary>
        ///     If there's a period in the SearchText property, remove text
        ///     to the end until you hit a period.  Otherwise, remove the
        ///     last character.  If the SearchText property is empty or null
        ///     return doing nothing.
        /// </summary>
        public void RemoveLastPartOfSearchText()
        {
            SearchText = RemoveLastPartOfText(SearchText);
        }

        /// <summary>
        ///     If there's a period in the argument, remove text
        ///     to the end until you hit a period.  Otherwise, remove the
        ///     last character.  If the argument is empty or null
        ///     return the empty string.
        /// </summary>
        /// <returns>The string cleaved of everything </returns>
        public static string RemoveLastPartOfText(string text)
        {
            while (true)
            {
                if (string.IsNullOrEmpty(text))
                    return text;

                var matches = Regex.Matches(text, Regex.Escape("."));

                // no period
                if (matches.Count == 0)
                    return "";

                if (matches[matches.Count - 1].Index + 1 != text.Length)
                    return text.Substring(0, matches[matches.Count - 1].Index + 2);

                // if period is in last position, remove that period and recurse
                text = text.Substring(0, text.Length - 1);
            }
        }

        public void OnSearchElementClicked(NodeModel nodeModel, Point position)
        {
            bool useDeafultPosition = position.X == 0 && position.Y == 0;

            dynamoViewModel.ExecuteCommand(new DynamoModel.CreateNodeCommand(
                nodeModel, position.X, position.Y, useDeafultPosition, true));

            OnRequestFocusSearch();
        }

        #endregion

        #region Commands

        public void Search(object parameter)
        {
            SearchAndUpdateResults();
        }

        internal bool CanSearch(object parameter)
        {
            return true;
        }

        internal void HideSearch(object parameter)
        {
            Visible = false;
        }

        internal bool CanHideSearch(object parameter)
        {
            return Visible;
        }

        public void ShowSearch(object parameter)
        {
            Visible = true;
        }

        internal bool CanShowSearch(object parameter)
        {
            return !Visible;
        }

        public void FocusSearch(object parameter)
        {
            OnRequestFocusSearch();
        }

        internal bool CanFocusSearch(object parameter)
        {
            return true;
        }

        internal void ToggleLayout(object parameter)
        {
            IsDetailedMode = (bool)parameter;
        }

        internal void UnSelectAllCategories()
        {
            foreach (var category in SearchCategories)
            {
                category.IsSelected = false;
            }
        }

        internal void SelectAllCategories(object parameter)
        {
            foreach (var category in SearchCategories)
            {
                category.IsSelected = true;
            }
        }

        /// <summary>
        /// Sets IsExpanded = false to open category and all subcategories.
        /// </summary>
        internal void CollapseAll(IEnumerable<NodeCategoryViewModel> categories)
        {
            while (categories != null)
            {
                var category = categories.FirstOrDefault(cat => cat.IsExpanded);

                if (category == null) break;
                category.IsExpanded = false;

                categories = category.SubCategories;
            }
        }

        /// <summary>
        /// This method is fired, when user clicks on class name in the search library view.
        /// </summary>
        /// <param name="className">Name of the class, that should be opened.</param>
        internal void OpenSelectedClass(string className)
        {
            // Clear search text.
            SearchText = String.Empty;
            CollapseAll(BrowserRootCategories);
            ClassNameToBeOpened = className;

            if (String.IsNullOrEmpty(className)) return;

            var categoryNames = className.Split(Configurations.CategoryDelimiterString.ToCharArray());

            IEnumerable<NodeCategoryViewModel> categories = BrowserRootCategories;
            foreach (var name in categoryNames)
            {
                var category = categories.FirstOrDefault(cat => cat.Name == name);
                if (category != null)
                {
                    category.IsExpanded = true;
                    categories = category.SubCategories;
                }
                // Category is null means that we are at the last level of hierarchy.
                // The next level is class level.
                else
                {
                    category = categories.FirstOrDefault(cat => cat is ClassesNodeCategoryViewModel);
                    if (category == null) break;
                    category.IsExpanded = true;

                    var classItem = category.Items.FirstOrDefault(item => item.Name == name) as NodeCategoryViewModel;
                    if (classItem == null) break;
                    classItem.IsExpanded = true;
                    break;
                }
            }
        }

        /// <summary>
        /// Attempts to find the node's icon, which is the same as its type name plus a Postfix, such as '.Small'.
        /// </summary>
        /// <returns>An ImageSource object pointing to the icon image for the NodeViewModel</returns>
        internal bool TryGetNodeIcon(NodeViewModel nodeViewModel, out ImageSource iconSource)
        {
            string nodeTypeName;
            string assemblyLocation;

            switch (nodeViewModel.NodeModel)
            {
                // For ZeroTouch nodes
                case DSFunction dsFunction:
                    FunctionDescriptor functionDescriptor = dsFunction.Controller.Definition;
                    assemblyLocation = functionDescriptor.Assembly;
                    nodeTypeName = Graph.Nodes.Utilities.GetFunctionDescriptorIconName(functionDescriptor);
                    break;
                // For DSVarArgFunctions like String.Concat
                case DSVarArgFunction dsVarArgFunction:
                    nodeTypeName = dsVarArgFunction.Controller.Definition.QualifiedName;
                    assemblyLocation = dsVarArgFunction.Controller.Definition.Assembly;
                    break;
                // For NodeModel nodes
                case NodeModel nodeModel:
                    nodeTypeName = nodeModel.GetType().FullName;
                    assemblyLocation = nodeModel.GetType().Assembly.Location;
                    break;
                default:
                    nodeTypeName = "";
                    assemblyLocation = "";
                    break;
            }

            iconSource = null;

            IconWarehouse currentWarehouse = iconServices.GetForAssembly(assemblyLocation);
            if (currentWarehouse is null) return false;

            iconSource = currentWarehouse.LoadIconInternal(nodeTypeName + Configurations.SmallIconPostfix);
            return !(iconSource is null);
        }

        #endregion
    }
}
