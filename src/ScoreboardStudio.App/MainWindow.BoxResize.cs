using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private const double FieldCornerGrabRadius=24;

    private static System.Windows.Input.Cursor FieldDragCursor(int corner) => corner<0 ? System.Windows.Input.Cursors.SizeAll :
        corner is 0 or 2 ? System.Windows.Input.Cursors.SizeNWSE : System.Windows.Input.Cursors.SizeNESW;

    // Clockwise: top left, top right, bottom right, bottom left; -1 moves the box.
    private static bool ContainsPoint(Crop box,Point p) =>
        p.X>=box.X && p.X<=box.X+box.Width && p.Y>=box.Y && p.Y<=box.Y+box.Height;

    private static int HitResizeCorner(Crop box,Point p,double toleranceX,double toleranceY)
    {
        Point[] corners=[new(box.X,box.Y),new(box.X+box.Width,box.Y),
            new(box.X+box.Width,box.Y+box.Height),new(box.X,box.Y+box.Height)];
        int hit=-1; double nearest=double.MaxValue;
        for(int i=0;i<corners.Length;i++)
        {
            double dx=Math.Abs(p.X-corners[i].X)/toleranceX,dy=Math.Abs(p.Y-corners[i].Y)/toleranceY;
            double distance=dx*dx+dy*dy;
            if(dx<=1 && dy<=1 && distance<nearest){hit=i;nearest=distance;}
        }
        return hit;
    }

    private static Crop ResizeOrMoveBox(Crop box,double dx,double dy,int corner,double minWidth,double minHeight)
    {
        if(corner<0)return box with {X=Math.Clamp(box.X+dx,0,1-box.Width),Y=Math.Clamp(box.Y+dy,0,1-box.Height)};
        double left=box.X,top=box.Y,right=left+box.Width,bottom=top+box.Height;
        // Keep the opposite corner anchored and prevent crossing or leaving the canvas.
        minWidth=Math.Min(minWidth,box.Width);minHeight=Math.Min(minHeight,box.Height);
        if(corner is 0 or 3)left=Math.Clamp(left+dx,0,right-minWidth);
        else right=Math.Clamp(right+dx,left+minWidth,1);
        if(corner is 0 or 1)top=Math.Clamp(top+dy,0,bottom-minHeight);
        else bottom=Math.Clamp(bottom+dy,top+minHeight,1);
        return new(left,top,right-left,bottom-top);
    }

    private void DrawResizeGrips(Canvas canvas,double x,double y,double width,double height,double units,string color)
    {
        foreach(var corner in new[]{new Point(x,y),new Point(x+width,y),new Point(x+width,y+height),new Point(x,y+height)})
        {
            double radius=7*units;
            var grip=new Ellipse {Width=radius*2,Height=radius*2,Fill=Brush(color),Stroke=Brush("#10251F"),StrokeThickness=2*units,IsHitTestVisible=false};
            Canvas.SetLeft(grip,corner.X-radius);Canvas.SetTop(grip,corner.Y-radius);
            Panel.SetZIndex(grip,100);canvas.Children.Add(grip);
        }
    }
}
