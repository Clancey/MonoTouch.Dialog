//
// DialogViewController.cs: drives MonoTouch.Dialog
//
// Author:
//   Miguel de Icaza
//
// Code to support pull-to-refresh based on Martin Bowling's TweetTableView
// which is based in turn in EGOTableViewPullRefresh code which was created
// by Devin Doty and is Copyrighted 2009 enormego and released under the
// MIT X11 license
//
using System;
using UIKit;
using CoreGraphics;
using System.Collections.Generic;
using System.Linq;
using Foundation;

namespace MonoTouch.Dialog
{
	public class DialogViewController : UITableViewController
	{
		public DialogViewController (IntPtr handle): base(handle)
		{
		}
		static readonly Version Version = new System.Version (UIDevice.CurrentDevice.SystemVersion);
		public UITableViewStyle Style = UITableViewStyle.Grouped;
		public UISearchBar searchBar;
		private UITableView tableView;
		public RefreshTableHeaderView refreshView;
		RootElement root;
		bool pushing;
		bool dirty;
		bool reloading;
		public event Action<NSIndexPath> OnSelection;

		/// <summary>
		/// The root element displayed by the DialogViewController, the value can be changed during runtime to update the contents.
		/// </summary>
		public RootElement Root {
			get {
				return root;
			}
			set {
				if (root == value)
					return;
				if (root != null)
					root.Dispose ();
				
				root = value;
				root.TableView = tableView;		
				ReloadData ();
			}
		} 

		EventHandler refreshRequested;
		/// <summary>
		/// If you assign a handler to this event before the view is shown, the
		/// DialogViewController will have support for pull-to-refresh UI.
		/// </summary>
		public event EventHandler RefreshRequested {
			add {
				if (tableView != null)
					throw new ArgumentException ("You should set the handler before the controller is shown");
				refreshRequested += value; 
			}
			remove {
				refreshRequested -= value;
			}
		}
		
		// If the value is 1, we are enabled, used in the source for quick computation
		bool enableSearch;
		public bool EnableSearch {
			get {
			   return enableSearch;
			}
			set {
				if (enableSearch == value)
					return;
				
				// After MonoTouch 3.0, we can allow for the search to be enabled/disable
			//	if (tableView != null)
			//		throw new ArgumentException ("You should set EnableSearch before the controller is shown");
				enableSearch = value;
			}
		}
		UIColor searchBarTintColor;
		public UIColor SearchBarTintColor {
			get {
				return searchBarTintColor;}
			set {
				if(searchBarTintColor == value)
					return;
				searchBarTintColor = value;
				if(searchBar == null)
					return;
				searchBar.TintColor = searchBarTintColor;
			}
		
		}
		
		// If set, we automatically scroll the content to avoid showing the search bar until 
		// the user manually pulls it down.
		public bool AutoHideSearch { get; set; }
		
		public string SearchPlaceholder { get; set; }
			
		/// <summary>
		/// Invoke this method to trigger a data refresh.   
		/// </summary>
		/// <remarks>
		/// This will invoke the RerfeshRequested event handler, the code attached to it
		/// should start the background operation to fetch the data and when it completes
		/// it should call ReloadComplete to restore the control state.
		/// </remarks>

		public void TriggerRefresh ()
		{
			TriggerRefresh (false);
		}
		
		void TriggerRefresh (bool showStatus)
		{

			if (refreshRequested == null)
				return;

			if (reloading)
				return;
			
			reloading = true;
			if (refreshView != null)
				refreshView.SetActivity (true);
			refreshRequested (this, EventArgs.Empty);

			if (reloading && showStatus && refreshView != null){
				UIView.BeginAnimations ("reloadingData");
				UIView.SetAnimationDuration (0.2);
				TableView.ContentInset = new UIEdgeInsets (60, 0, 0, 0);
				UIView.CommitAnimations ();
			}
		}
		
		/// <summary>
		/// Invoke this method to signal that a reload has completed, this will update the UI accordingly.
		/// </summary>
		public void ReloadComplete ()
		{
			if (Version.Major >= 6) {
				
				RefreshControl.AttributedTitle = new NSAttributedString(String.Format ("Last Updated: {0:g}",DateTime.Now));
				RefreshControl.EndRefreshing();
				return;
			}
			if (refreshView != null)
				refreshView.LastUpdate = DateTime.Now;
			if (!reloading)
				return;

			reloading = false;
			if (refreshView == null)
				return;
			
			refreshView.SetActivity (false);
			refreshView.Flip (false);
			UIView.BeginAnimations ("doneReloading");
			UIView.SetAnimationDuration (0.3f);
			TableView.ContentInset = new UIEdgeInsets (0, 0, 0, 0);
			refreshView.SetStatus (RefreshViewStatus.PullToReload);
			UIView.CommitAnimations ();
		}
		
		/// <summary>
		/// Controls whether the DialogViewController should auto rotate
		/// </summary>
		public bool Autorotate { get; set; }
		
		public bool IncludeIndex { get; set; }
		public bool CombineSectionIndex {get;set;}
		
		public override bool ShouldAutorotateToInterfaceOrientation (UIInterfaceOrientation toInterfaceOrientation)
		{
			return Autorotate || toInterfaceOrientation == UIInterfaceOrientation.Portrait;
		}
		
		public override void DidRotate (UIInterfaceOrientation fromInterfaceOrientation)
		{
			base.DidRotate (fromInterfaceOrientation);
			ReloadData ();
		}
		
		Section [] originalSections;
		Element [][] originalElements;
		public int SectionCount
		{
			get {return Root.Sections.Count();}
		}
		
		public int RowsInSection( int section) {
				return Root.Sections[section].Count;
		}
		
		
		/// <summary>
		/// Allows caller to programatically activate the search bar and start the search process
		/// </summary>
		public void StartSearch ()
		{
			if (originalSections != null)
				return;
			
			searchBar.BecomeFirstResponder ();
			originalSections = Root.Sections.ToArray ();
			originalElements = new Element [originalSections.Length][];
			for (int i = 0; i < originalSections.Length; i++)
				originalElements [i] = originalSections [i].Elements.ToArray ();
		}
		
		
		/// <summary>
		/// Allows the caller to programatically stop searching.
		/// </summary>
		public virtual void FinishSearch ()
		{
			if (originalSections == null)
				return;
			
			Root.Sections = new List<Section> (originalSections);
			originalSections = null;
			originalElements = null;
			//searchBar.BecomeFirstResponder();
			searchBar.ResignFirstResponder();
			ReloadData ();
		}
		
		public delegate void SearchTextEventHandler (object sender, SearchChangedEventArgs args);
		public event SearchTextEventHandler SearchTextChanged;
		
		public virtual void OnSearchTextChanged (string text)
		{
			if (SearchTextChanged != null)
				SearchTextChanged (this, new SearchChangedEventArgs (text));
		}
		
		public void PerformFilter (string text)
		{
			if (originalSections == null)
				return;
			
			OnSearchTextChanged (text);
			
			var newSections = new List<Section> ();
			
			for (int sidx = 0; sidx < originalSections.Length; sidx++){
				Section newSection = null;
				var section = originalSections [sidx];
				Element [] elements = originalElements [sidx];
				
				for (int eidx = 0; eidx < elements.Length; eidx++){
					if (elements [eidx].Matches (text)){
						if (newSection == null){
							newSection = new Section (section.Caption){
								FooterView = section.FooterView,
								HeaderView = section.HeaderView
							};
							newSections.Add (newSection);
						}
						newSection.Add (elements [eidx]);
					}
				}
			}
			
			Root.Sections = newSections;
			
			ReloadData ();
		}
		
		public virtual void SearchButtonClicked (string text)
		{
		}
			
		class SearchDelegate : UISearchBarDelegate {
			DialogViewController container;
			
			public SearchDelegate (DialogViewController container)
			{
				this.container = container;
			}
			
			public override void OnEditingStarted (UISearchBar searchBar)
			{
				searchBar.ShowsCancelButton = true;
				container.StartSearch ();
			}
			
			public override void OnEditingStopped (UISearchBar searchBar)
			{
				searchBar.ShowsCancelButton = false;
				container.FinishSearch ();
			}
			
			public override void TextChanged (UISearchBar searchBar, string searchText)
			{
				container.PerformFilter (searchText ?? "");
			}
			
			public override void CancelButtonClicked (UISearchBar searchBar)
			{
				searchBar.ShowsCancelButton = false;
				container.FinishSearch ();
				searchBar.ResignFirstResponder ();
			}
			
			public override void SearchButtonClicked (UISearchBar searchBar)
			{
				container.SearchButtonClicked (searchBar.Text);
			}
		}
		
		public class Source : UITableViewSource {
			const float yboundary = 65;
			protected DialogViewController Container;
			protected RootElement Root;
			bool checkForRefresh;
			
			public Source (DialogViewController container)
			{
				this.Container = container;
				Root = container.root;
			}
//			public override float GetHeightForRow (UITableView tableView, MonoTouch.Foundation.NSIndexPath indexPath)
//			{
//				var section = Root.Sections [indexPath.Section];
//				var element = section.Elements [indexPath.Row];
//				
//				var sizable = element as IElementSizing;
//				if (sizable == null)
//					return tableView.RowHeight;
//				return sizable.GetHeight (tableView, indexPath);
//			}
			public override nint RowsInSection (UITableView tableview, nint section)
			{
				var s = Root.Sections [(int)section];
				var count = s.Collapsed ? 0 : s.Elements.Count;
				
				return count;
			}

			public override nint NumberOfSections (UITableView tableView)
			{
				return Root.Sections.Count;
			}

			public override string TitleForHeader (UITableView tableView, nint section)
			{
				return Root.Sections [(int)section].Caption;
			}

			public override string TitleForFooter (UITableView tableView, nint section)
			{
				return Root.Sections [(int)section].Footer;
			}

			public override UITableViewCell GetCell (UITableView tableView, Foundation.NSIndexPath indexPath)
			{
				var section = Root.Sections [indexPath.Section];
				var element = section.Elements [indexPath.Row];
				element.Row = indexPath.Row;
				return element.GetCell (tableView);
			}
			
			public override void WillDisplay (UITableView tableView, UITableViewCell cell, NSIndexPath indexPath)
			{
				if (Root.NeedColorUpdate){
					var section = Root.Sections [indexPath.Section];
					var element = section.Elements [indexPath.Row];
					var colorized = element as IColorizeBackground;
					if (colorized != null)
						colorized.WillDisplay (tableView, cell, indexPath);
				}
			}
			
			public override void RowSelected (UITableView tableView, NSIndexPath indexPath)
			{
				var onSelection = Container.OnSelection;
				if (onSelection != null)
					onSelection (indexPath);
				Container.Selected (indexPath);
			}	
			public override void RowDeselected (UITableView tableView, NSIndexPath indexPath)
			{
				Container.Deselected(indexPath);
			}
			
			public override void AccessoryButtonTapped (UITableView tableView, NSIndexPath indexPath)
			{
				
				Container.AccessoryButtonTapped(indexPath);
			}
			
			public override UIView GetViewForHeader (UITableView tableView, nint sectionIdx)
			{
				var section = Root.Sections [(int)sectionIdx];
				section.tableView = tableView;
				section.Index = sectionIdx;
				return section.HeaderView;
			}

			public override nfloat GetHeightForHeader (UITableView tableView, nint sectionIdx)
			{
				var section = Root.Sections [(int)sectionIdx];
				if (section.HeaderView == null)
					return -1;
				return section.HeaderView.Frame.Height;
			}

			public override UIView GetViewForFooter (UITableView tableView, nint sectionIdx)
			{
				var section = Root.Sections [(int)sectionIdx];
				return section.FooterView;
			}
			
			public override nfloat GetHeightForFooter (UITableView tableView, nint sectionIdx)
			{
				var section = Root.Sections [(int)sectionIdx];
				if (section.FooterView == null)
					return -1;
				return section.FooterView.Frame.Height;
			}
			/*
			NSArray array;
			public override string[] SectionIndexTitles (UITableView tableView)
			{
				if(Container.IncludeIndex)
				{
						string[] titleArray; 
					if(Container.CombineSectionIndex)
						titleArray = Root.Sections.Select(x => x.IndexString).Distinct().ToArray();
					else
						titleArray = Root.Sections.Select(x => x.IndexString).ToArray();
					array = NSArray.FromStrings (titleArray);
			
					
				}
				
			    return array;
			}
			*/
			public override nint SectionFor (UITableView tableView, string title, nint atIndex)
			{
				if(Container.CombineSectionIndex && !string.IsNullOrEmpty(title) )
					return Root.Sections.Where(x=> x.IndexString == title).First().Index;
				return atIndex;
			}
			
			
			
			#region Pull to Refresh support
			public override void Scrolled (UIScrollView scrollView)
			{
				if(DialogViewController.Version.Major >= 6)
					return;
				if (!checkForRefresh)
					return;
				if (Container.reloading)
					return;
				var view  = Container.refreshView;
				if (view == null)
					return;
				
				var point = Container.TableView.ContentOffset;
				
				if (view.IsFlipped && point.Y > -yboundary && point.Y < 0){
					view.Flip (true);
					view.SetStatus (RefreshViewStatus.PullToReload);
				} else if (!view.IsFlipped && point.Y < -yboundary){
					view.Flip (true);
					view.SetStatus (RefreshViewStatus.ReleaseToReload);
				}
			}
			
			public override void DraggingStarted (UIScrollView scrollView)
			{
				if(DialogViewController.Version.Major >= 6)
					return;
				checkForRefresh = true;
			}
			
			public override void DraggingEnded (UIScrollView scrollView, bool willDecelerate)
			{
				if(DialogViewController.Version.Major >=6)
					return;
				if (Container.refreshView == null)
					return;
				
				checkForRefresh = false;
				if (Container.TableView.ContentOffset.Y > -yboundary)
					return;
				Container.TriggerRefresh (true);
			}
			#endregion
		}
		
		//
		// Performance trick, if we expose GetHeightForRow, the UITableView will
		// probe *every* row for its size;   Avoid this by creating a separate
		// model that is used only when we have items that require resizing
		//
		public class SizingSource : Source {
			public SizingSource (DialogViewController controller) : base (controller) {
				//Console.WriteLine("Sizing Source");
			}
			
			public override nfloat GetHeightForRow (UITableView tableView, NSIndexPath indexPath)
			{
				var section = Root.Sections [indexPath.Section];
				var element = section.Elements [indexPath.Row];
				
				var sizable = element as IElementSizing;
				if (sizable == null)
					return tableView.RowHeight;
				return sizable.GetHeight (tableView, indexPath);
			}
		}
			
		/// <summary>
		/// Activates a nested view controller from the DialogViewController.   
		/// If the view controller is hosted in a UINavigationController it
		/// will push the result.   Otherwise it will show it as a modal
		/// dialog
		/// </summary>
		public void ActivateController (UIViewController controller)
		{
			dirty = true;
			
			var parent = ParentViewController;
			var nav = parent as UINavigationController;
			
			// We can not push a nav controller into a nav controller
			if (nav != null && !(controller is UINavigationController))
				nav.PushViewController (controller, true);
			else
				PresentModalViewController (controller, true);
		}

		/// <summary>
		/// Dismisses the view controller.   It either pops or dismisses
		/// based on the kind of container we are hosted in.
		/// </summary>
		public void DeactivateController (bool animated)
		{
			var parent = ParentViewController;
			var nav = parent as UINavigationController;
			
			if (nav != null)
				nav.PopViewController (animated);
			else
				DismissViewControllerAsync (animated);
		}

		void SetupSearch ()
		{
			if (enableSearch){
				searchBar = new UISearchBar (new CGRect (0, 0, tableView.Bounds.Width, 44)) {
					Delegate = new SearchDelegate (this),
					TintColor = searchBarTintColor
				};
				if (SearchPlaceholder != null)
					searchBar.Placeholder = this.SearchPlaceholder;
				//tableView.TableHeaderView = new UIView(new CGRect(0,0,tableView.Bounds.Width,100));
				tableView.TableHeaderView = searchBar;
			} else {
				// Does not work with current Monotouch, will work with 3.0
				 tableView.TableHeaderView = null;
			}
		}
		
		public virtual void Selected (NSIndexPath indexPath)
		{
			var section = root.Sections [indexPath.Section];
			var element = section.Elements [indexPath.Row];

			element.Selected (this, tableView, indexPath);
		}
		public virtual void Deselected(NSIndexPath indexPath)
		{
			
			var section = root.Sections [indexPath.Section];
			var element = section.Elements [indexPath.Row];

			element.Deselected (this, tableView, indexPath);
		}
		
		public virtual void AccessoryButtonTapped(NSIndexPath indexPath)
		{
			
			var section = root.Sections [indexPath.Section];
			var element = section.Elements [indexPath.Row];

			element.AccessoryButtonTapped (this, tableView, indexPath);
		}
		
		public virtual UITableView MakeTableView (CGRect bounds, UITableViewStyle style)
		{
			return new UITableView (bounds, style);
		}
		
		public override void LoadView ()
		{
			tableView = MakeTableView (UIScreen.MainScreen.Bounds, Style);
			tableView.AutoresizingMask = UIViewAutoresizing.FlexibleHeight | UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleTopMargin;
			tableView.AutosizesSubviews = true;
			tableView.SectionIndexMinimumDisplayRowCount = 0;
			
			UpdateSource ();
			View = tableView;
			SetupSearch ();
			ConfigureTableView ();
			
			if (root == null)
				return;
			root.TableView = tableView;
		}
		
		void ConfigureTableView ()
		{
			if (refreshRequested != null){
				// The dimensions should be large enough so that even if the user scrolls, we render the
				// whole are with the background color.
				var bounds = View.Bounds;
				if(DialogViewController.Version.Major >= 6)
				{
					RefreshControl = new UIRefreshControl();
					RefreshControl.AttributedTitle = new NSAttributedString("Pull down to refresh...");
					RefreshControl.ValueChanged += delegate {
						refreshRequested(this, EventArgs.Empty);
					};
				}
				else {
					refreshView = MakeRefreshTableHeaderView (new CGRect (0, -bounds.Height, bounds.Width, bounds.Height));
					if (reloading)
						refreshView.SetActivity (true);
						TableView.AddSubview (refreshView);
				}
			}
		}
		
		public virtual RefreshTableHeaderView MakeRefreshTableHeaderView (CGRect rect)
		{
			return new RefreshTableHeaderView (rect);
		}

		public override void ViewWillAppear (bool animated)
		{
			base.ViewWillAppear (animated);
			if (AutoHideSearch){
				if (enableSearch){
					if (TableView.ContentOffset.Y < 44)
						TableView.ContentOffset = new CGPoint (0, 44);
				}
			}
			if (root == null)
				return;
			
			root.Prepare ();
			
			NavigationItem.HidesBackButton = !pushing;
			if (root.Caption != null)
				NavigationItem.Title = root.Caption;
			if (dirty){
				tableView.ReloadData ();
				dirty = false;
			}
		}


		public virtual Source CreateSizingSource (bool unevenRows)
		{
			//return new Source (this);
			return unevenRows ? new SizingSource (this) : new Source (this);
		}
		
		Source TableSource;
		
		void UpdateSource ()
		{
			if (root == null)
				return;
			
			TableSource = CreateSizingSource (root.UnevenRows);
			tableView.Source = TableSource;
		}

		public void ReloadData ()
		{
			if (root == null)
				return;
			
			root.Prepare ();
			if (tableView != null){
				UpdateSource ();
				try{
				tableView.ReloadData ();
				}
				catch(Exception ex)
				{
					Console.WriteLine(ex);
				}
			}
			dirty = false;
		}
		
		public event EventHandler ViewDissapearing;
		
		public override void ViewWillDisappear (bool animated)
		{
			FinishSearch();
			if (ViewDissapearing != null)
				ViewDissapearing (this, EventArgs.Empty);
		}

		void PrepareRoot (RootElement root)
		{
			this.root = root;
			if (root != null)
				root.Prepare ();
		}
		
		public DialogViewController (RootElement root) : base (UITableViewStyle.Grouped)
		{
			PrepareRoot (root);
		}
		
		public DialogViewController (UITableViewStyle style, RootElement root) : base (style)
		{
			Style = style;
			PrepareRoot (root);
		}
		
		/// <summary>
		///     Creates a new DialogViewController from a RootElement and sets the push status
		/// </summary>
		/// <param name="root">
		/// The <see cref="RootElement"/> containing the information to render.
		/// </param>
		/// <param name="pushing">
		/// A <see cref="System.Boolean"/> describing whether this is being pushed 
		/// (NavigationControllers) or not.   If pushing is true, then the back button 
		/// will be shown, allowing the user to go back to the previous controller
		/// </param>
		public DialogViewController (RootElement root, bool pushing) : base (UITableViewStyle.Grouped)
		{
			this.pushing = pushing;
			PrepareRoot (root);
		}

		public DialogViewController (UITableViewStyle style, RootElement root, bool pushing) : base (style)
		{
			this.pushing = pushing;
			PrepareRoot (root);
		}
	}
	
}
