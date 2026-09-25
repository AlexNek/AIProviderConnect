using System.Windows;

using FluentAssertions;

using GraphVisualization.ViewModels;

namespace ScraperTool.Tests;

public class GraphViewerViewModelTests
{
    [Fact]
    public void FitToView_WithValidSizes_ScalesAndCentersContent()
    {
        // Arrange
        var vm = new GraphViewerViewModel();
        vm.SetViewportSize(400, 200);

        // Act
        vm.SetContentSize(800, 200);

        // Assert: scale = min(400/800, 200/200) = 0.5, horizontally centered
        vm.Scale.Should().Be(0.5);
        vm.OffsetX.Should().Be((400 - 800 * 0.5) / 2);
        vm.OffsetY.Should().Be((200 - 200 * 0.5) / 2);
    }

    [Fact]
    public void FitToView_BeforeViewportHasRealSize_RetriesOnSizeUpdate()
    {
        // Arrange: content arrives while the viewport still has zero size
        var vm = new GraphViewerViewModel();
        vm.SetContentSize(800, 200);
        vm.Scale.Should().Be(1);

        // Act: layout produces a real viewport size later
        vm.SetViewportSize(400, 200);

        // Assert
        vm.Scale.Should().Be(0.5);
    }

    [Fact]
    public void ZoomAt_ClampsScale_ToMinMaxBounds()
    {
        // Arrange
        var vm = new GraphViewerViewModel();
        var pivot = new Point(100, 100);

        // Act
        for (var i = 0; i < 50; i++)
            vm.ZoomAt(pivot, 1.5);

        // Assert
        vm.Scale.Should().Be(GraphViewerViewModel.MaxScale);

        // Act
        for (var i = 0; i < 50; i++)
            vm.ZoomAt(pivot, 0.5);

        // Assert
        vm.Scale.Should().Be(GraphViewerViewModel.MinScale);
    }

    [Fact]
    public void ZoomAt_KeepsPivotPoint_StableUnderTransform()
    {
        // Arrange
        var vm = new GraphViewerViewModel();
        vm.SetViewportSize(800, 600);
        vm.SetContentSize(400, 300);
        var pivot = new Point(250, 180);

        // Content point currently under the pivot (in content coordinates)
        var contentX = (pivot.X - vm.OffsetX) / vm.Scale;
        var contentY = (pivot.Y - vm.OffsetY) / vm.Scale;

        // Act
        vm.ZoomAt(pivot, 1.7);

        // Assert: the same content point is still under the pivot
        (vm.OffsetX + contentX * vm.Scale).Should().BeApproximately(pivot.X, 1e-9);
        (vm.OffsetY + contentY * vm.Scale).Should().BeApproximately(pivot.Y, 1e-9);
    }

    [Fact]
    public void ZoomInCommand_ZoomsAroundViewportCenter()
    {
        // Arrange
        var vm = new GraphViewerViewModel();
        vm.SetViewportSize(800, 600);

        // Act
        vm.ZoomInCommand.Execute(null);

        // Assert
        vm.Scale.Should().BeApproximately(1.2, 1e-9);
    }

    [Fact]
    public void ActualSizeCommand_ResetsScaleToOne_AndCentersContent()
    {
        // Arrange
        var vm = new GraphViewerViewModel();
        vm.SetViewportSize(800, 600);
        vm.SetContentSize(1600, 1200);
        vm.Scale.Should().NotBe(1);

        // Act
        vm.ActualSizeCommand.Execute(null);

        // Assert
        vm.Scale.Should().Be(1);
        vm.OffsetX.Should().Be((800 - 1600) / 2);
        vm.OffsetY.Should().Be((600 - 1200) / 2);
    }

    [Fact]
    public void Pan_TranslatesOffsets_ByMouseDelta()
    {
        // Arrange
        var vm = new GraphViewerViewModel();
        var start = new Point(50, 40);

        // Act
        vm.BeginPan(start);
        vm.UpdatePan(new Point(80, 55));
        vm.EndPan();

        // Assert
        vm.OffsetX.Should().Be(30);
        vm.OffsetY.Should().Be(15);

        // Act: movement after EndPan is ignored
        vm.UpdatePan(new Point(200, 200));

        // Assert
        vm.OffsetX.Should().Be(30);
        vm.OffsetY.Should().Be(15);
    }
}
