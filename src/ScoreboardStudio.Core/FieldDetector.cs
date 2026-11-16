namespace ScoreboardStudio.Core;

// Bright-region grouping only. No digit recognition, model, or external service.
public static class FieldDetector
{
    public static List<Crop> Detect(VideoFrame frame, int threshold = 130, string isolation = "Bright colors")
    {
        if (isolation == "White highlights")
        {
            var panels = DetectWarmPanels(frame, threshold);
            if (panels.Count >= 2) return panels;
        }
        int step = Math.Max(1, frame.Width / 640), w = frame.Width / step, h = frame.Height / step;
        var mask = new bool[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int p = (y * step * frame.Width + x * step) * 4;
            mask[y * w + x] = frame.Pixels[p + 3] > 32 && PixelIsolation.Level(frame.Pixels[p], frame.Pixels[p + 1], frame.Pixels[p + 2], isolation) >= threshold;
        }
        // Integral image makes rectangular dilation O(pixels), independent of radius.
        var integral = new int[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            int row = 0;
            for (int x = 0; x < w; x++)
            {
                row += mask[y * w + x] ? 1 : 0;
                integral[(y + 1) * (w + 1) + x + 1] = integral[y * (w + 1) + x + 1] + row;
            }
        }
        int rx = Math.Max(3, w / 70), ry = Math.Max(2, h / 55);
        var connected = new bool[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int x0 = Math.Max(0, x - rx), x1 = Math.Min(w, x + rx + 1), y0 = Math.Max(0, y - ry), y1 = Math.Min(h, y + ry + 1);
            connected[y * w + x] = integral[y1 * (w + 1) + x1] - integral[y0 * (w + 1) + x1] - integral[y1 * (w + 1) + x0] + integral[y0 * (w + 1) + x0] > 0;
        }
        var result = new List<Crop>();
        var queue = new Queue<int>();
        for (int start = 0; start < connected.Length; start++)
        {
            if (!connected[start]) continue;
            connected[start] = false;
            queue.Enqueue(start);
            int minX = w, minY = h, maxX = 0, maxY = 0, lit = 0;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % w, y = i / w;
                if (mask[i]) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); lit++; }
                Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);
            }
            int width = maxX - minX + 1, height = maxY - minY + 1;
            if (lit < 12 || height < Math.Max(5, h / 60) || width < 3 || width > w * .9 || height > h * .6) continue;
            result.Add(new Crop((minX - 4.0) / w, (minY - 4.0) / h, (width + 8.0) / w, (height + 8.0) / h).Clamp());
        }
        return result.OrderBy(r => Math.Round(r.Y * 10)).ThenBy(r => r.X).Take(24).ToList();

        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = y * w + x;
            if (!connected[i]) return;
            connected[i] = false; queue.Enqueue(i);
        }
    }

    // Backlit red/amber display windows provide more stable boundaries than lit
    // digits alone, and reserve space for currently unlit digit positions.
    private static List<Crop> DetectWarmPanels(VideoFrame frame, int threshold)
    {
        int step=Math.Max(1,frame.Width/640), w=frame.Width/step,h=frame.Height/step;
        var mask=new bool[w*h];
        for(int y=0;y<h;y++) for(int x=0;x<w;x++)
        {
            int i=(y*step*frame.Width+x*step)*4;
            int b=frame.Pixels[i],g=frame.Pixels[i+1],r=frame.Pixels[i+2];
            mask[y*w+x]=frame.Pixels[i+3]>32 && r>90 && r-g>40 && r-b>60;
        }
        var joined=new bool[mask.Length];
        for(int y=0;y<h;y++) for(int x=0;x<w;x++)
        {
            if(!mask[y*w+x]) continue;
            for(int dy=-2;dy<=2;dy++) for(int dx=-2;dx<=2;dx++)
                if(x+dx>=0 && x+dx<w && y+dy>=0 && y+dy<h) joined[(y+dy)*w+x+dx]=true;
        }
        var result=new List<Crop>();var queue=new Queue<int>();
        for(int start=0;start<joined.Length;start++)
        {
            if(!joined[start]) continue;
            joined[start]=false;queue.Enqueue(start);
            int left=w,top=h,right=0,bottom=0,count=0;
            while(queue.Count>0)
            {
                int i=queue.Dequeue(),x=i%w,y=i/w;
                if(mask[i]) { left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);count++; }
                Visit(x-1,y);Visit(x+1,y);Visit(x,y-1);Visit(x,y+1);
            }
            int pw=right-left+1,ph=bottom-top+1;
            if(right>=w-1 && pw<w*.06 || left==0 && pw<w*.06) continue;
            if(count<40 || ph<h*.09 || ph>h*.45 || pw<w*.025 || pw>w*.8 || pw/(double)ph>3.5 && ph<h*.16) continue;
            AddPanel(left,top,right,bottom,0);
        }
        return result.OrderBy(r=>Math.Round(r.Y*10)).ThenBy(r=>r.X).Take(24).ToList();

        void AddPanel(int left,int top,int right,int bottom,int depth)
        {
            // Refine each band's colored boundary after splitting a merged label
            // and score window. This does not assume a sport or field location.
            int refinedLeft=w,refinedTop=h,refinedRight=0,refinedBottom=0;
            for(int y=top;y<=bottom;y++) for(int x=left;x<=right;x++)
                if(mask[y*w+x]) { refinedLeft=Math.Min(refinedLeft,x);refinedRight=Math.Max(refinedRight,x);refinedTop=Math.Min(refinedTop,y);refinedBottom=Math.Max(refinedBottom,y); }
            left=refinedLeft;right=refinedRight;top=refinedTop;bottom=refinedBottom;
            int pw=right-left+1,ph=bottom-top+1;
            if(ph<h*.09 || pw<3 || pw/(double)ph>3.5 && ph<h*.16) return;
            if(depth<3)
            {
                int previous=-1;
                for(int y=top;y<=bottom;y++)
                {
                    int lit=0;
                    for(int x=left;x<=right;x++)
                    { int p=(y*step*frame.Width+x*step)*4;if(PixelIsolation.Level(frame.Pixels[p],frame.Pixels[p+1],frame.Pixels[p+2],"White highlights")>threshold) lit++; }
                    if(lit<Math.Max(2,pw/50)) continue;
                    if(previous>=0 && y-previous>Math.Max(8,h*.025))
                    {
                        int cut=(y+previous)/2;
                        AddPanel(left,top,right,cut,depth+1);AddPanel(left,cut+1,right,bottom,depth+1);return;
                    }
                    previous=y;
                }
            }
            // Find large gaps between bright digit groups inside a shared panel
            // (for example player number and player fouls). Keep blank margins.
            var columns=new List<int>();
            for(int x=left;x<=right;x++)
            {
                int bright=0;
                for(int y=top;y<=bottom;y++)
                {
                    int i=(y*step*frame.Width+x*step)*4;
                    if(PixelIsolation.Level(frame.Pixels[i],frame.Pixels[i+1],frame.Pixels[i+2],"White highlights")>threshold) bright++;
                }
                if(bright>=Math.Max(2,ph/15)) columns.Add(x);
            }
            if(columns.Count<3) return;
            var cuts=new List<int> { left };
            for(int i=1;i<columns.Count;i++)
                if(columns[i]-columns[i-1]>Math.Max(12,ph*.45)) cuts.Add((columns[i]+columns[i-1])/2);
            cuts.Add(right+1);
            for(int i=0;i<cuts.Count-1;i++)
                result.Add(new Crop(cuts[i]/(double)w,top/(double)h,(cuts[i+1]-cuts[i])/(double)w,ph/(double)h).Clamp());
        }
        void Visit(int x,int y)
        {
            if(x<0 || y<0 || x>=w || y>=h) return;
            int i=y*w+x;if(!joined[i]) return;joined[i]=false;queue.Enqueue(i);
        }
    }
}
