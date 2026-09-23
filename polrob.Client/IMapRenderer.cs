using polrob.Shared;
using SkiaSharp;

namespace polrob.Client;

public interface IMapRenderer : IDisposable
{
    void DrawBackground(SKCanvas canvas, SKRect visible);
    void DrawProps(SKCanvas canvas, SKRect visible, System.Drawing.PointF? viewer = null);
    void DrawCollisionOverlay(SKCanvas canvas, GameMap map);
}
