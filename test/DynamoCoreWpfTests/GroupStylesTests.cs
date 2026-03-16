using Dynamo.Configuration;
using Dynamo.Nodes;
using Dynamo.Utilities;
using Dynamo.ViewModels;
using Dynamo.Views;
using Dynamo.Wpf.Views;
using DynamoCoreWpfTests.Utility;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace DynamoCoreWpfTests
{
    [TestFixture]
    public class GroupStylesTests : DynamoTestUIBase
    {
        public AnnotationView NodeViewWithGuid(string guid)
        {
            var annotationView =
                View.WorkspaceTabs.ChildrenOfType<WorkspaceView>().First().ChildrenOfType<AnnotationView>();
            var annotationViewOfType = annotationView.Where(x => x.ViewModel.AnnotationModel.GUID.ToString() == guid);
            Assert.AreEqual(1, annotationViewOfType.Count(), "Expected a single Annotation View with guid: " + guid);

            return annotationViewOfType.First();
        }

        private int GetGroupStyleContextOptionCount(AnnotationView annotationView)
        {
            // Manually create and open the group context menu (normally triggered by right-click).
            annotationView.CreateAndAttachAnnotationPopup();
            annotationView.GroupContextMenuPopup.IsOpen = true;
            DispatcherUtil.DoEvents();

            var stylesSubmenu = annotationView.GroupStyleSelectorGrid as Grid;
            Assert.IsNotNull(stylesSubmenu, "Styles sub-menu border not found.");

            var border = stylesSubmenu.Children.OfType<Border>().FirstOrDefault();
            var popup = stylesSubmenu.Children.OfType<Popup>().FirstOrDefault();

            Assert.IsNotNull(border, "Sub-menu border not found.");
            Assert.IsNotNull(popup, "Sub-menu popup not found.");

            // Trigger MouseEnter to populate the popup content.
            border.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = Mouse.MouseEnterEvent
            });
            DispatcherUtil.DoEvents();

            Assert.IsTrue(popup.IsOpen, "Popup did not open after MouseEnter.");
            Assert.IsInstanceOf<Border>(popup.Child, "Popup content is not a Border.");

            var wrapper = popup.Child as Border;
            var stackPanel = wrapper.Child as StackPanel;
            Assert.IsNotNull(stackPanel, "Could not find StackPanel inside popup.");

            return stackPanel.Children
                .OfType<Border>()
                .Select(b => b.Child)
                .OfType<StackPanel>()
                .Count();
        }

        private bool IsGroupStyleSubmenuOpen(AnnotationView annotationView)
        {
            annotationView.CreateAndAttachAnnotationPopup();
            annotationView.GroupContextMenuPopup.IsOpen = true;
            DispatcherUtil.DoEvents();

            var stylesSubmenu = annotationView.GroupStyleSelectorGrid as Grid;
            Assert.IsNotNull(stylesSubmenu, "Styles sub-menu border not found.");

            var border = stylesSubmenu.Children.OfType<Border>().FirstOrDefault();
            var popup = stylesSubmenu.Children.OfType<Popup>().FirstOrDefault();

            Assert.IsNotNull(border, "Sub-menu border not found.");
            Assert.IsNotNull(popup, "Sub-menu popup not found.");

            border.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = Mouse.MouseEnterEvent
            });
            DispatcherUtil.DoEvents();

            return popup.IsOpen;
        }

        public override void Open(string path)
        {
            base.Open(path);
            DispatcherUtil.DoEvents();
        }

        public override void Run()
        {
            base.Run();

            DispatcherUtil.DoEvents();
        }

        protected override void GetLibrariesToPreload(List<string> libraries)
        {
            libraries.Add("VMDataBridge.dll");
            libraries.Add("ProtoGeometry.dll");
            base.GetLibrariesToPreload(libraries);
        }

        /// <summary>
        /// Validates that the GroupStyles in the PreferencesView match the ones in the AnnotationView
        /// </summary>
        [Test]
        public void TestDefaultGroupStyles_PreferencesView()
        {
            int defaultGroupStylesCounter = 4;
            Open(@"UI\GroupTest.dyn");

            //Creates the Preferences dialog and the ScaleFactor = 2 ( Medium)
            var preferencesWindow = new PreferencesView(View);
            preferencesWindow.Show();
            DispatcherUtil.DoEvents();

            var prefViewModel = preferencesWindow.DataContext as PreferencesViewModel;

            var annotationView = NodeViewWithGuid("a432d63f-7a36-45ad-b30a-7924beb20e90");

            var annotationViewModel = annotationView.DataContext as AnnotationViewModel;

            //Close the Preferences Dialog
            preferencesWindow.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            DispatcherUtil.DoEvents();

            //Check that the GroupStyles in the AnnotationView match the ones in the PreferencesView (default ones)
            Assert.AreEqual(annotationViewModel.GroupStyleList.OfType<GroupStyleItem>().Count(), prefViewModel.StyleItemsList.Count);

        }

        /// <summary>
        /// Add a new GroupStyle and validates that the GroupStyles in the PreferencesView match the ones in the AnnotationView
        /// </summary>
        [Test]
        public void TestAddGroupStyle_ContextMenu()
        {
            int currentGroupStylesCounter = 5;
            Open(@"UI\GroupTest.dyn");

            var preferencesSettings = (View.DataContext as DynamoViewModel).PreferenceSettings;

            //Creates the Preferences dialog and the ScaleFactor = 2 ( Medium)
            var preferencesWindow = new PreferencesView(View);
            preferencesWindow.Show();
            DispatcherUtil.DoEvents();

            var prefViewModel = preferencesWindow.DataContext as PreferencesViewModel;

            var annotationView = NodeViewWithGuid("a432d63f-7a36-45ad-b30a-7924beb20e90");

            var annotationViewModel = annotationView.DataContext as AnnotationViewModel;

            //Check that the GroupStyles in the AnnotationView match the ones in the PreferencesView
            Assert.AreEqual(annotationViewModel.GroupStyleList.OfType<GroupStyleItem>().Count(), prefViewModel.StyleItemsList.Count);

            //Add one Custom Group Style to the PreferencesView
            preferencesSettings.GroupStyleItemsList.Add(new Dynamo.Configuration.GroupStyleItem { Name = "Custom 1", HexColorString = "FFFF00", IsDefault = false });
            
            //Close the Preferences Dialog 
            preferencesWindow.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            DispatcherUtil.DoEvents();

            var innerStackCount = GetGroupStyleContextOptionCount(annotationView);

            //Check that the GroupStyles in the AnnotationView match the ones in the PreferencesView
            Assert.AreEqual(innerStackCount, currentGroupStylesCounter);
        }

        [Test]
        public void TestHideDefaultGroupStyles_ContextMenu_IsDisabledWhenCustomStylesExist()
        {
            Open(@"UI\GroupTest.dyn");

            var preferencesSettings = (View.DataContext as DynamoViewModel).PreferenceSettings;
            preferencesSettings.GroupStyleItemsList.Add(new GroupStyleItem
            {
                Name = "Custom 1",
                HexColorString = "FFFF00",
                IsDefault = false,
                GroupStyleId = Guid.NewGuid(),
                FontSize = 36
            });
            preferencesSettings.ShowDefaultGroupStyles = false;

            var preferencesWindow = new PreferencesView(View);
            preferencesWindow.Show();
            DispatcherUtil.DoEvents();

            var prefViewModel = preferencesWindow.DataContext as PreferencesViewModel;
            Assert.AreEqual(5, prefViewModel.StyleItemsList.Count);
            Assert.AreEqual(4, prefViewModel.StyleItemsList.Count(style => style.IsDefault));

            var annotationView = NodeViewWithGuid("a432d63f-7a36-45ad-b30a-7924beb20e90");
            var isSubmenuOpen = IsGroupStyleSubmenuOpen(annotationView);
            Assert.IsFalse(isSubmenuOpen, "Group Style submenu should be disabled when defaults are hidden and custom styles exist.");

            preferencesWindow.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            DispatcherUtil.DoEvents();
        }

        [Test]
        public void TestHideDefaultGroupStyles_ContextMenu_IsDisabledWhenNoCustomStylesExist()
        {
            Open(@"UI\GroupTest.dyn");

            var preferencesSettings = (View.DataContext as DynamoViewModel).PreferenceSettings;
            preferencesSettings.ShowDefaultGroupStyles = false;

            var annotationView = NodeViewWithGuid("a432d63f-7a36-45ad-b30a-7924beb20e90");
            var isSubmenuOpen = IsGroupStyleSubmenuOpen(annotationView);
            Assert.IsFalse(isSubmenuOpen, "Group Style submenu should be disabled when defaults are hidden.");
        }

        [Test]
        public void TestLegacyDefaultStyles_DoNotDuplicateContextMenuEntries()
        {
            Open(@"UI\GroupTest.dyn");

            var preferencesSettings = (View.DataContext as DynamoViewModel).PreferenceSettings;
            preferencesSettings.GroupStyleItemsList = GroupStyleItem.DefaultGroupStyleItems
                .Select(defaultStyle => new GroupStyleItem
                {
                    Name = defaultStyle.Name,
                    HexColorString = defaultStyle.HexColorString,
                    FontSize = defaultStyle.FontSize,
                    GroupStyleId = Guid.Empty,
                    IsDefault = false
                })
                .ToList();

            var preferencesWindow = new PreferencesView(View);
            preferencesWindow.Show();
            DispatcherUtil.DoEvents();

            var prefViewModel = preferencesWindow.DataContext as PreferencesViewModel;
            Assert.AreEqual(4, prefViewModel.StyleItemsList.Count, "Preferences should normalize to one set of defaults.");

            var annotationView = NodeViewWithGuid("a432d63f-7a36-45ad-b30a-7924beb20e90");
            var contextMenuStyleCount = GetGroupStyleContextOptionCount(annotationView);
            Assert.AreEqual(4, contextMenuStyleCount, "Context menu should only show one set of default styles.");

            preferencesWindow.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            DispatcherUtil.DoEvents();
        }

        [Test]
        public void TestDuplicatedDefaultStyles_DoNotDuplicateContextMenuEntries()
        {
            Open(@"UI\GroupTest.dyn");

            var preferencesSettings = (View.DataContext as DynamoViewModel).PreferenceSettings;
            var duplicatedDefaults = GroupStyleItem.DefaultGroupStyleItems
                .Concat(GroupStyleItem.DefaultGroupStyleItems.Select(defaultStyle => new GroupStyleItem
                {
                    Name = defaultStyle.Name,
                    HexColorString = defaultStyle.HexColorString,
                    FontSize = defaultStyle.FontSize,
                    GroupStyleId = Guid.NewGuid(),
                    IsDefault = true
                }))
                .ToList();

            preferencesSettings.GroupStyleItemsList = duplicatedDefaults;

            var annotationView = NodeViewWithGuid("a432d63f-7a36-45ad-b30a-7924beb20e90");
            var contextMenuStyleCount = GetGroupStyleContextOptionCount(annotationView);
            Assert.AreEqual(4, contextMenuStyleCount, "Context menu should only show canonical default styles once.");
        }

        [Test]
        public void TestRepeatedContextMenuOpen_DoesNotAccumulateDefaultStyles()
        {
            Open(@"UI\GroupTest.dyn");

            var annotationView = NodeViewWithGuid("a432d63f-7a36-45ad-b30a-7924beb20e90");

            var firstCount = GetGroupStyleContextOptionCount(annotationView);
            annotationView.GroupContextMenuPopup.IsOpen = false;
            DispatcherUtil.DoEvents();

            var secondCount = GetGroupStyleContextOptionCount(annotationView);
            annotationView.GroupContextMenuPopup.IsOpen = false;
            DispatcherUtil.DoEvents();

            Assert.AreEqual(4, firstCount);
            Assert.AreEqual(4, secondCount);
        }

        [Test]
        public void CustomColorPicker_PrePopulateDefaultColors()
        {
            Open(@"UI\GroupTest.dyn");
            var tabName = "Visual Settings";
            var expanderName = "Group Styles";

            var preferencesSettings = (View.DataContext as DynamoViewModel).PreferenceSettings;

            //Creates the Preferences dialog 
            var preferencesWindow = new PreferencesView(View);
            
            //Validate the 4 default existing styles list
            Assert.AreEqual(preferencesSettings.GroupStyleItemsList.Count, 4);

            //Adds two Custom Group Styles to the PreferencesView
            preferencesSettings.GroupStyleItemsList.Add(new GroupStyleItem { Name = "Custom 1", HexColorString = "FFFF00", IsDefault = false });
            preferencesSettings.GroupStyleItemsList.Add(new GroupStyleItem { Name = "Custom 2", HexColorString = "FF00FF", IsDefault = false });

            //Finds the Visual Settings tab and open it
            var tabControl = preferencesWindow.preferencesTabControl;
            if (tabControl == null) return;
            var preferencesTab = (from TabItem tabItem in tabControl.Items
                                    where tabItem.Header.ToString().Equals(tabName)
                                    select tabItem).FirstOrDefault();
            if (preferencesTab == null) return;
            tabControl.SelectedItem = preferencesTab;

            //Finds the Group Styles section and open it 
            var listExpanders = WpfUtilities.ChildrenOfType<Expander>(preferencesTab.Content as ScrollViewer);
            var tabExpander = (from expander in listExpanders
                                where expander.Header.ToString().Equals(expanderName)
                                select expander).FirstOrDefault();
            if (tabExpander == null) return;
            tabExpander.IsExpanded = true;
                   
                 
            preferencesWindow.Show();
            DispatcherUtil.DoEvents();

            //Click the AddStyle button
            preferencesWindow.AddStyleButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            DispatcherUtil.DoEvents();

            //The custom Colors list used by ColorPicker is empty
            Assert.AreEqual(preferencesWindow.stylesCustomColors.Count, 0);

            //Clicks the color button, this will populate the custom Colors list with the style colors added in Group Styles
            preferencesWindow.buttonColorPicker.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            DispatcherUtil.DoEvents();

            //Validates that the 3 added custom colors are present in the custom Colors list
            Assert.AreEqual(preferencesWindow.stylesCustomColors.Count, 2);
        }

        [Test]
        public void ResetStylesButton_RemovesCustomStylesOnly()
        {
            Open(@"UI\GroupTest.dyn");

            var preferencesWindow = new PreferencesView(View);
            preferencesWindow.Show();
            DispatcherUtil.DoEvents();

            var prefViewModel = preferencesWindow.DataContext as PreferencesViewModel;
            Assert.AreEqual(Visibility.Collapsed, preferencesWindow.ResetStylesButton.Visibility);

            prefViewModel.AddStyle(new StyleItem
            {
                Name = "Custom Style 1",
                HexColorString = "FFFF00",
                FontSize = 36,
                GroupStyleId = Guid.NewGuid(),
                IsDefault = false
            });

            prefViewModel.AddStyle(new StyleItem
            {
                Name = "Custom Style 2",
                HexColorString = "00FFFF",
                FontSize = 36,
                GroupStyleId = Guid.NewGuid(),
                IsDefault = false
            });

            DispatcherUtil.DoEvents();
            Assert.AreEqual(6, prefViewModel.StyleItemsList.Count);
            Assert.IsTrue(prefViewModel.CanResetGroupStyles);
            Assert.AreEqual(Visibility.Visible, preferencesWindow.ResetStylesButton.Visibility);

            preferencesWindow.ResetStylesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            DispatcherUtil.DoEvents();

            Assert.AreEqual(4, prefViewModel.StyleItemsList.Count);
            Assert.IsTrue(prefViewModel.StyleItemsList.All(style => style.IsDefault));
            Assert.IsFalse(prefViewModel.CanResetGroupStyles);
            Assert.AreEqual(Visibility.Collapsed, preferencesWindow.ResetStylesButton.Visibility);
        }
    }
}
