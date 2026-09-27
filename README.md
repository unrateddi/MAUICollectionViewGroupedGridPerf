# .NET MAUI CollectionView GroupedGridPerf

Minimal .NET MAUI (Android) reproduction for two performance problems in `CollectionView` with `GridItemsLayout`:

1. **Regression in 10.0.100** — `SpacingItemDecoration.GetItemOffsets` calls the uncached `SpanSizeLookup.GetSpanGroupIndex(itemCount - 1)` for **every cell on every layout pass**, an O(n) walk per cell. Assigning a 10 000‑item grouped source blocks the UI thread for ~40 s (94 ms on 10.0.90); a finger fling raises an ANR. Introduced by [dotnet/maui#35782](https://github.com/dotnet/maui/pull/35782). [dotnet/maui#38341](https://github.com/dotnet/maui/pull/38341) fixed a crash on the same path but not the cost.
   Issue: https://github.com/dotnet/maui/issues/38925
2. **Long‑standing** — span lookups over positions the `GridLayoutManager` has not cached are O(position) per cell, because every `getSpanSize` is a JNI call into `GridLayoutSpanSizeLookup`; with a grouped source `ObservableGroupedSource.GetGroupAndIndex` is itself O(position), making them O(position²). ~38 s to `ScrollTo` the last of 10 000 grouped items on 10.0.90; ungrouped 10.0.90 stutters (27 % janky frames) when scrolling up after a jump to the bottom.
   Issue: https://github.com/dotnet/maui/issues/38926

The project is `dotnet new maui` with the `MainPage` replaced; no third‑party packages. Only `net10.0-android` is targeted.

## Build & run

```powershell
# pick the MAUI version to test; clean between versions
Remove-Item bin, obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet build -t:Run -f net10.0-android -c Release -p:MauiVersion=10.0.100
```

Tested versions: `10.0.90`, `10.0.100`, `10.0.110`, `11.0.0-rc.1.26451.6` (the last one needs the .NET 11 SDK and `-f net11.0-android`; change `TargetFrameworks` accordingly and set `SupportedOSPlatformVersion` to 24.0).

## Steps

The page shows a grouped 4‑column grid (50 groups) and a status label with the time the UI thread was blocked by the last action (measured from the action until a `Dispatcher.Dispatch` callback runs).

| Control | Purpose |
|---|---|
| `1.000 / 10.000 Items` switch | 50 × 20 vs 50 × 200 items. Re‑assigns `ItemsSource`. |
| `Grouped` switch | Same data flattened to one list (`IsGrouped=false`). Isolates the cost of `ObservableGroupedSource`. |
| `Add Spacing` switch | `Horizontal/VerticalItemSpacing` 0 ↔ 5. The regression reproduces with **0** spacing. |
| `Go to Bottom` | `ScrollTo(lastItem, groupIndex: lastGroup, End, animate: false)` |
| `Go to Top` | `ScrollTo(0, groupIndex: 0, Start, animate: false)` |

1. Toggle **10.000 Items** → read "Setting 10.000 items: UI thread blocked … ms".
2. Tap **Go to Bottom**, then **Go to Top** → read the times.
3. Toggle **Grouped** off and repeat 1–2.
4. Fling the list with a finger while it is loading → ANR dialog after 5 s (10.0.100+).
5. Fling smoothness: `adb shell dumpsys gfxinfo com.companyname.groupedgridperf reset`, fling 6 times, `adb shell dumpsys gfxinfo com.companyname.groupedgridperf`.

## Results

HONOR PGT‑N19 (Android 16, API 36), Release builds. All values are UI‑thread‑blocked milliseconds. Full data in [measurements.txt](measurements.txt).

**Grouped, 10 000 items**

| MAUI | Set ItemsSource | Scroll to bottom | Scroll to top |
|---|---|---|---|
| 10.0.90 | **94** | 37 748 | 91 |
| 10.0.100 | **40 125** | 99 296 | 43 934 |
| 10.0.110 | 39 085 | 96 911 | 43 145 |
| 11.0.0‑rc.1.26451.6 | 8 125 | 20 097 | 8 993 |

**Ungrouped, 10 000 items**

| MAUI | Set ItemsSource | Scroll to bottom | Scroll to top |
|---|---|---|---|
| 10.0.90 | 97 | 1 077 | 92 |
| 10.0.100 | 1 186 | 2 555 | 1 159 |
| 10.0.110 | 1 200 | 2 519 | 1 131 |
| 11.0.0‑rc.1.26451.6 | 346 | 675 | 387 |

**Scaling, 10.0.100, Set ItemsSource, 1 000 → 10 000 items:** ungrouped 225 → 1 186 ms (×5, linear); grouped 696 → 40 125 ms (×58, super‑linear).

- "Set ItemsSource" 10.0.90 → 10.0.100 shows problem 1 (427× slower grouped, 12× ungrouped).
- "Scroll to bottom" grouped vs ungrouped **on 10.0.90** (37 748 vs 1 077 ms) shows problem 2, which predates the regression.
- 11.0.0‑rc.1 is faster only because of the CoreCLR runtime; it is still ~86× slower than 10.0.90 and the code path is unchanged on `main`.

**Finger flings, ungrouped, 10 000 items** — 6 flings, `dumpsys gfxinfo` frame stats. Raw output in [finger-fling-stutter.txt](finger-fling-stutter.txt).

| MAUI | Flinging | Frames rendered | Janky | p50 | p90 | p95 | p99 |
|---|---|---|---|---|---|---|---|
| 10.0.90 | down from top | 562 | 1.8 % | 5 ms | 6 ms | 6 ms | 10 ms |
| 10.0.90 | up from bottom | 281 | 27.1 % | 5 ms | 81 ms | 117 ms | 200 ms |
| 10.0.100 | down from top | **51** | 29.4 % | 6 ms | **1 100 ms** | 1 450 ms | 2 800 ms |
| 10.0.100 | up from bottom | **35** | 22.9 % | 6 ms | **1 750 ms** | 3 400 ms | 4 950 ms |

"Frames rendered" for the same 6 flings drops from 562 to 51: on 10.0.100 the UI thread is blocked for most of the gesture, so the janky‑frame percentage understates the problem — compare frame counts and p90/p99.

- 10.0.90 down‑from‑top is smooth; 10.0.100 is not → problem 1 also affects **ungrouped** grids (the decoration walks to `itemCount - 1` for every new cell).
- 10.0.90 up‑from‑bottom stutters → problem 2's ungrouped face. Before #35782 the span caches were off, so `GridLayoutManager` computed each cell's span index by walking `getSpanSize(0…position)` across JNI; cheap near the top, ~10 000 calls per cell near the bottom. #35782 turned the caches on (correct) but added the from‑the‑end walk (regression). The caches only help for positions already laid out, so scrolling **up** into an uncached region still walks from 0.

## ANR evidence

While flinging the 10 000‑item grouped grid on the 11.0.0‑rc.1 build, Android raised an ANR (`Input dispatching timed out … Waited 5000ms for MotionEvent`). The main thread had consumed 28 s of CPU (`utm=2800`) and was inside the regressed path:

```
"main" prio=5 tid=1 Native
  at GridLayoutSpanSizeLookup.n_getSpanSize (Native method)
  at GridLayoutSpanSizeLookup.getSpanSize
  at androidx.recyclerview.widget.GridLayoutManager$SpanSizeLookup.getSpanGroupIndex (GridLayoutManager.java:1742)
  at SpacingItemDecoration.n_getItemOffsets (Native method)
  at SpacingItemDecoration.getItemOffsets
  at androidx.recyclerview.widget.RecyclerView.getItemDecorInsetsForChild
  at androidx.recyclerview.widget.RecyclerView$LayoutManager.calculateItemDecorationsForChild
  at androidx.recyclerview.widget.GridLayoutManager.layoutChunk
  at androidx.recyclerview.widget.LinearLayoutManager.fill / scrollBy / scrollVerticallyBy
  at androidx.recyclerview.widget.RecyclerView$ViewFlinger.run
  at android.view.Choreographer.doFrame
```

- [anr-trace-rc1-pid28250.txt](anr-trace-rc1-pid28250.txt) — full thread dump from `/data/anr`
- [logcat-anr-rc1-pid28250.txt](logcat-anr-rc1-pid28250.txt) — `system`/`events` log lines for the process (ANR, dialogs, kill)

## Root cause pointers

- `src/Controls/src/Core/Handlers/Items/Android/SpacingItemDecoration.cs` — `GetItemOffsets` → `GetSpanGroupIndex(itemCount - 1, spanCount)`; the decoration is attached even when spacing is 0.
- `GridLayoutManager.SpanSizeLookup.getSpanGroupIndex` (AndroidX) — iterates `getSpanSize(i)` from the nearest cached key to the target; O(n) for the last position.
- `src/Controls/src/Core/Handlers/Items/Android/GridLayoutSpanSizeLookup.cs` — `GetSpanSize` → `adapter.GetItemViewType(position)`; one JNI round‑trip per call. Span caches were enabled only in #35782; `GetSpanIndex`/`GetSpanGroupIndex` are not overridden, so uncached positions are resolved by AndroidX walking `getSpanSize` from the nearest lower cached key (from 0 when scrolling up into a cold region).
- `src/Controls/src/Core/Handlers/Items/Android/ItemsSources/ObservableGroupedSource.cs` — `IsGroupHeader`/`IsGroupFooter`/`GetItem` → `GetGroupAndIndex`, a one‑step‑per‑position `while` loop; `Count` is O(groups) and called from `IsFooter` on every lookup.

## Notes

- Item strings are unique across groups (`G{i} Item {j}`) on purpose: grouped `ScrollTo` resolves its target via `GetPositionForItem`, which returns the first `Equals` match across all groups.
- The ungrouped source is materialised with `.ToList()`; a lazy `SelectMany` is not `IList` and would add its own O(n) `Count` cost.