using System.Collections.ObjectModel;
using System.Diagnostics;

namespace GroupedGridPerf
{
    public partial class MainPage : ContentPage
    {
        // Items must be unique across groups: grouped ScrollTo resolves the target by item Equals.
        private readonly ObservableCollection<Group> ThousandItemGroups = [.. Enumerable.Range(1, 50).Select(i => new Group($"Group {i}", Enumerable.Range(1, 20).Select(j => $"G{i} Item {j}")))];
        private readonly ObservableCollection<Group> TenThousandItemGroups = [.. Enumerable.Range(1, 50).Select(i => new Group($"Group {i}", Enumerable.Range(1, 200).Select(j => $"G{i} Item {j}")))];

        ObservableCollection<Group> CurrentGroups => LoadItemsSwitch.IsToggled ? TenThousandItemGroups : ThousandItemGroups;

        public MainPage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            Measure($"Setting 1.000 items", () => Cv.ItemsSource = ThousandItemGroups);
        }

        void OnLoadItemsSwitchToggled(object sender, ToggledEventArgs e)
        {
            Measure($"Setting {(e.Value ? "10.000" : "1.000")} items", () => Cv.ItemsSource = GroupedSwitch.IsToggled ? CurrentGroups : CurrentGroups.SelectMany(g => g).ToList());
        }

        void OnSpacingSwitchToggled(object sender, ToggledEventArgs e)
        {
            GridLayout.HorizontalItemSpacing = e.Value ? 5 : 0;
            GridLayout.VerticalItemSpacing = e.Value ? 5 : 0;
        }

        void OnGroupedSwitchToggled(object sender, ToggledEventArgs e)
        {
            Measure($"Setting {(e.Value ? "Grouped" : "Ungrouped")} items", () =>
            {
                Cv.IsGrouped = e.Value;
                Cv.ItemsSource = e.Value ? CurrentGroups : CurrentGroups.SelectMany(g => g).ToList();
            });
        }

        void OnGoToTopClicked(object sender, EventArgs e)
        {
            Measure("ScrollTo Top", () =>
            {
                Cv.ScrollTo(0, groupIndex: Cv.IsGrouped ? 0 : -1, ScrollToPosition.Start, animate: false);
            });
        }

        void OnGoToBottomClicked(object sender, EventArgs e)
        {
            var groups = CurrentGroups;

            Measure("ScrollTo Bottom", () =>
            {
                if (Cv.IsGrouped)
                {
                    Cv.ScrollTo(groups[^1].Count - 1, groupIndex: groups.Count - 1, ScrollToPosition.End, animate: false);
                }
                else
                {
                    Cv.ScrollTo(groups.Sum(g => g.Count) - 1, position: ScrollToPosition.End, animate: false);
                }
            });
        }

        void Measure(string label, Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            Dispatcher.Dispatch(() =>
            {
                sw.Stop();
                Status.Text = $"{label}: UI thread blocked {sw.ElapsedMilliseconds} ms";
                Debug.WriteLine($"[Repro] {Status.Text}");
            });
        }
    }
}
