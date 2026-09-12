using SkiaSharp;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("Usage: <character-animation-docs> <output-directory>");
var work = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (output == Path.Combine(work, "source") || output == Path.Combine(work, "generated"))
    throw new ArgumentException("Output must not overwrite source artwork or generated layers.");
Directory.CreateDirectory(output);
const int size = 1024;
const float bodyWidth = 512;
var audit = new List<object>();
foreach (var role in new[] { "police", "robber" })
{
    using var generated = Load(Path.Combine(work, "generated", $"{role}-body.png"));
    using var isolated = Matte(generated, fillHoles: role == "police");
    var bounds = Bounds(isolated);
    var scale = bodyWidth / bounds.Width;
    var target = SKRect.Create(256, 512 - bounds.Height * scale / 2, 512, bounds.Height * scale);
    using var body = NewBitmap();
    using (var canvas = new SKCanvas(body)) DrawRegion(canvas, isolated, bounds, target);
    Save(body, Path.Combine(output, $"{role}-body.png"));

    using var source = Load(Path.Combine(work, "source", $"char_{role}_run_2.png"));
    using var armPath = new SKPath();
    if (role == "police")
    {
        armPath.MoveTo(478, 269);
        armPath.CubicTo(516, 273, 551, 318, 542, 372);
        armPath.CubicTo(536, 412, 503, 442, 467, 434);
        armPath.CubicTo(445, 432, 437, 413, 453, 389);
        armPath.CubicTo(474, 354, 490, 301, 478, 269);
    }
    else
    {
        armPath.MoveTo(475, 259);
        armPath.CubicTo(513, 264, 554, 299, 544, 358);
        armPath.CubicTo(543, 397, 508, 448, 470, 446);
        armPath.CubicTo(442, 445, 433, 423, 447, 399);
        armPath.CubicTo(476, 351, 490, 288, 475, 259);
    }
    armPath.Close();
    using var arm = Clip(source, armPath, false);
    var names = new List<string>();
    for (var frame = 0; frame <= 8; frame++)
    {
        var name = frame == 0 ? $"char_{role}.png" : $"char_{role}_run_{frame}.png";
        using var result = NewBitmap();
        using var canvas = new SKCanvas(result);
        var phase = frame == 0 ? 0 : MathF.Sin((frame - 1) * MathF.PI / 4);
        for (var side = -1; side <= 1; side += 2)
        {
            canvas.Save();
            canvas.Translate(512 + side * 225, role == "police" ? 510 : 525);
            canvas.Scale(side, 1);
            canvas.RotateDegrees(phase * side * 12);
            canvas.Scale(1.28f, 1.28f * (1 + phase * side * .13f));
            canvas.Translate(-482, -280);
            Draw(canvas, arm, new SKRect(0, 0, source.Width, source.Height));
            canvas.Restore();
        }
        canvas.DrawBitmap(body, 0, 0);
        SaveAndValidate(result, body, name, 0);
        names.Add(name);
    }

    var special = role == "police" ? "arrest" : "surrend";
    using var specialSource = Load(Path.Combine(work, "source", $"char_{role}_{special}.png"));
    using var specialPath = new SKPath();
    if (role == "police")
    {
        specialPath.MoveTo(279, 351);
        specialPath.CubicTo(227, 440, 237, 577, 298, 636);
        specialPath.CubicTo(327, 675, 374, 708, 430, 733);
        specialPath.LineTo(497, 658);
        specialPath.LineTo(491, 616);
        specialPath.CubicTo(400, 569, 360, 498, 320, 358);
        specialPath.Close();
        using var mirrored = new SKPath(specialPath);
        mirrored.Transform(SKMatrix.CreateScale(-1, 1, 512, 0));
        specialPath.AddPathReverse(mirrored);
        using var hands=new SKPath(); hands.AddRoundRect(new SKRect(417, 618, 610, 788), 50, 50);
        Union(specialPath,hands);
        using var prop=new SKPath(); prop.AddRect(new SKRect(462, 655, 565, 876));
        Union(specialPath,prop);
    }
    else
    {
        specialPath.MoveTo(35, 192);
        specialPath.LineTo(220, 192);
        specialPath.LineTo(220, 380);
        specialPath.CubicTo(233, 448, 257, 534, 287, 592);
        specialPath.LineTo(266, 618);
        specialPath.CubicTo(169, 583, 109, 495, 72, 389);
        specialPath.LineTo(26, 415);
        specialPath.Close();
        using var mirrored = new SKPath(specialPath);
        mirrored.Transform(SKMatrix.CreateScale(-1, 1, 512, 0));
        specialPath.AddPath(mirrored);
    }
    using var specialArms = Clip(specialSource, specialPath, true);
    using var specialLayer = NewBitmap();
    using (var canvas = new SKCanvas(specialLayer))
    {
        if (role == "police")
        {
            var specialScale = 512f / 498f;
            canvas.Translate(512 - 512 * specialScale, target.Top - 154 * specialScale);
            canvas.Scale(specialScale);
        }
        else canvas.Translate(0, target.Top - 190);
        if (role == "police") Draw(canvas, specialArms, new SKRect(0, 0, 1024, 1024));
        else
        {
            DrawRegion(canvas, specialArms, new SKRect(0,0,512,1024), new SKRect(25,0,537,1024));
            DrawRegion(canvas, specialArms, new SKRect(512,0,1024,1024), new SKRect(487,0,999,1024));
        }
    }
    var specialName = $"char_{role}_{special}.png";
    ComposeSpecial(specialLayer, body, specialName, role == "police" ? 675 : 1024);
    names.Add(specialName);
    if (role == "robber")
    {
        using var jailGenerated = Load(Path.Combine(work, "generated", "robber-jailbreak-arms.png"));
        using var jailArms = Matte(jailGenerated, true);
        using var jailLayer = NewBitmap();
        using (var canvas = new SKCanvas(jailLayer))
        {
            var jailScale = .58f * 1254 / jailArms.Width;
            canvas.Translate(512 - jailArms.Width * jailScale / 2, 430 - 270 * .58f);
            canvas.Scale(jailScale);
            Draw(canvas, jailArms, new SKRect(0, 0, jailArms.Width, jailArms.Height));
        }
        const string jailName = "char_robber_prison_break.png";
        ComposeSpecial(jailLayer, body, jailName, 675);
        names.Add(jailName);
    }
    RenderSheet(role, names);
}
File.WriteAllText(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Built and checked {audit.Count} normalized frames.");

void ComposeSpecial(SKBitmap arms, SKBitmap body, string name, int frontStart)
{
    using var result = NewBitmap();
    using var canvas = new SKCanvas(result);
    Draw(canvas, arms, new SKRect(0, 0, size, size));
    canvas.DrawBitmap(body, 0, 0);
    if (frontStart < 1024)
    {
        // The unchanged head occludes forward-reaching limbs along its
        // natural curved chin. Draw the body only once so its transparent
        // eye openings and antialiased silhouette retain identical alpha.
        using var head = new SKPath();
        head.MoveTo(0, 0); head.LineTo(1024, 0); head.LineTo(1024, 472);
        head.LineTo(739, 472);
        head.CubicTo(739, 590, 650, 700, 512, 700);
        head.CubicTo(374, 700, 285, 590, 285, 472);
        head.LineTo(0, 472); head.Close();
        canvas.Save(); canvas.ClipPath(head, SKClipOperation.Difference, antialias: true);
        canvas.DrawBitmap(arms,0,0); canvas.Restore();
    }
    SaveAndValidate(result, body, name, frontStart);
}

void Union(SKPath target,SKPath shape)
{
    using var combined=target.Op(shape,SKPathOp.Union)??throw new InvalidOperationException("Path union failed");
    target.Reset();target.AddPath(combined);
}

void SaveAndValidate(SKBitmap sprite, SKBitmap body, string name, int foregroundStart)
{
    var changed = 0;
    for (var y = 0; y < size; y++)
    for (var x = 0; x < size; x++)
    {
        var reference = body.GetPixel(x, y);
        // Forward limbs may cover the torso. The central head/face must
        // remain identical, including all idle/run pixels across the body.
        var protectedPixel = foregroundStart == 0 || foregroundStart == 1024
            || x >= 330 && x < 694 && y < 605;
        if (reference.Alpha == 255 && protectedPixel && sprite.GetPixel(x, y) != reference) changed++;
    }
    if (changed != 0) throw new InvalidOperationException($"Body changed in {name}: {changed}");
    var bounds = Bounds(sprite);
    if (bounds.Left < 8 || bounds.Top < 8 || bounds.Right > 1016 || bounds.Bottom > 1016)
        throw new InvalidOperationException($"Clipped pose: {name} {bounds}");
    Save(sprite, Path.Combine(output, name));
    var topology = CountComponents(sprite);
    if (topology.Count != 1) throw new InvalidOperationException($"Detached artwork in {name}: {string.Join(",", topology)}");
    audit.Add(new { name, width = size, height = size, bodyWidth, pivot = new[] {512,512},
        comparisonRegion = foregroundStart is 0 or 1024 ? "opaque shared body" : "central head, excluding foreground arms",
        changedProtectedPixels = changed, connectedComponentsAtAlpha32 = topology.Count,
        bounds = new[] {bounds.Left,bounds.Top,bounds.Right,bounds.Bottom} });
}

List<int> CountComponents(SKBitmap image)
{
    var seen=new bool[image.Width*image.Height]; var counts=new List<int>();
    for(var i=0;i<seen.Length;i++)
    {
        if(seen[i]||image.GetPixel(i%image.Width,i/image.Width).Alpha<32)continue;
        var queue=new List<int>{i};seen[i]=true;
        for(var j=0;j<queue.Count;j++)foreach(var n in Neighbors(queue[j],image.Width,image.Height))
        {
            if(seen[n]||image.GetPixel(n%image.Width,n/image.Width).Alpha<32)continue;
            seen[n]=true;queue.Add(n);
        }
        counts.Add(queue.Count);
    }
    return counts;
}

SKBitmap Matte(SKBitmap source, bool fillHoles)
{
    var width = source.Width;
    var height = source.Height;
    var mask = new bool[width * height];
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    {
        var p = source.GetPixel(x, y);
        mask[y * width + x] = p.Alpha > 200 && Math.Min(p.Red, Math.Min(p.Green,p.Blue)) < 140;
    }
    // Generated checkerboard previews are not alpha. Keep only substantial
    // outlined components; closed light skin/metal interiors belong to them.
    var seen = new bool[mask.Length];
    for (var i = 0; i < mask.Length; i++)
    {
        if (seen[i] || !mask[i]) continue;
        var component = new List<int> {i}; seen[i] = true;
        for (var j = 0; j < component.Count; j++) foreach (var n in Neighbors(component[j],width,height))
            if (!seen[n] && mask[n]) { seen[n] = true; component.Add(n); }
        if (component.Count < 500) foreach (var n in component) mask[n] = false;
    }
    if (fillHoles)
    {
        var outside = new bool[mask.Length];
        var queue = new Queue<int>();
        for (var i = 0; i < mask.Length; i++) if (!mask[i] && (i < width || i >= mask.Length-width || i%width==0 || i%width==width-1))
        { outside[i] = true; queue.Enqueue(i); }
        while (queue.TryDequeue(out var i)) foreach (var n in Neighbors(i,width,height))
            if (!outside[n] && !mask[n]) { outside[n] = true; queue.Enqueue(n); }
        for (var i = 0; i < mask.Length; i++)
        {
            var p=source.GetPixel(i%width,i/width);
            // Keep neutral checkerboard holes (eyes/lock arch) transparent;
            // enclosed warm skin highlights are part of the drawing.
            var neutralHole = !mask[i] && Math.Max(p.Red,Math.Max(p.Green,p.Blue))-Math.Min(p.Red,Math.Min(p.Green,p.Blue)) < 20;
            mask[i] = !outside[i] && !neutralHole;
        }
    }
    var coverage = Smooth(mask, width, height);
    var result = new SKBitmap(width,height,SKColorType.Rgba8888,SKAlphaType.Unpremul);
    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
    {
        var alpha = (byte)Math.Clamp((coverage[y*width+x]-.15f)/.7f*255,0,255);
        var color=source.GetPixel(x,y);
        if (alpha>0 && alpha<255)
        {
            var found=false;
            for(var radius=1;radius<=4 && !found;radius++)
            for(var dy=-radius;dy<=radius && !found;dy++)
            for(var dx=-radius;dx<=radius;dx++)
            {
                var nx=x+dx;var ny=y+dy;
                if(nx<1||ny<1||nx>=width-1||ny>=height-1||coverage[ny*width+nx]<.99f)continue;
                color=source.GetPixel(nx,ny);found=true;break;
            }
        }
        result.SetPixel(x,y,alpha == 0 ? SKColors.Transparent : color.WithAlpha(alpha));
    }
    return result;
}

SKBitmap Clip(SKBitmap source, SKPath path, bool clean)
{
    using var mask = new SKBitmap(source.Width, source.Height);
    using (var canvas = new SKCanvas(mask))
    {
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint {Color=SKColors.White,IsAntialias=true};
        canvas.DrawPath(path,paint);
    }
    var result = new SKBitmap(source.Width,source.Height,SKColorType.Rgba8888,SKAlphaType.Unpremul);
    var solid = new bool[source.Width * source.Height];
    for(var y=0;y<source.Height;y++) for(var x=0;x<source.Width;x++)
        solid[y*source.Width+x] = source.GetPixel(x,y).Alpha >= 250 && mask.GetPixel(x,y).Alpha > 127;
    var coverage = clean ? Smooth(solid,source.Width,source.Height) : null;
    for(var y=0;y<source.Height;y++) for(var x=0;x<source.Width;x++)
    {
        var original=source.GetPixel(x,y);
        var alpha=clean ? (byte)(255*Math.Clamp((coverage![y*source.Width+x]-.3f)/.4f,0,1))
            : (byte)(original.Alpha*mask.GetPixel(x,y).Alpha/255);
        result.SetPixel(x,y,alpha==0?SKColors.Transparent:original.WithAlpha(alpha));
    }
    return result;
}

float[] Smooth(bool[] mask,int width,int height)
{
    float[] kernel=[1,4,6,4,1];
    var temp=new float[mask.Length]; var result=new float[mask.Length];
    for(var y=0;y<height;y++) for(var x=0;x<width;x++) for(var k=-2;k<=2;k++)
        temp[y*width+x]+=(mask[y*width+Math.Clamp(x+k,0,width-1)]?1:0)*kernel[k+2]/16;
    for(var y=0;y<height;y++) for(var x=0;x<width;x++) for(var k=-2;k<=2;k++)
        result[y*width+x]+=temp[Math.Clamp(y+k,0,height-1)*width+x]*kernel[k+2]/16;
    return result;
}

IEnumerable<int> Neighbors(int i,int width,int height)
{
    if(i%width>0)yield return i-1; if(i%width+1<width)yield return i+1;
    if(i>=width)yield return i-width; if(i+width<width*height)yield return i+width;
}
SKRect Bounds(SKBitmap image)
{
    var left=image.Width;var top=image.Height;var right=0;var bottom=0;
    for(var y=0;y<image.Height;y++)for(var x=0;x<image.Width;x++)if(image.GetPixel(x,y).Alpha>=32)
    {left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x+1);bottom=Math.Max(bottom,y+1);}
    return new SKRect(left,top,right,bottom);
}
SKBitmap NewBitmap(){var b=new SKBitmap(size,size,SKColorType.Rgba8888,SKAlphaType.Premul);b.Erase(SKColors.Transparent);return b;}
SKBitmap Load(string path)=>SKBitmap.Decode(path)??throw new IOException(path);
void Draw(SKCanvas canvas,SKBitmap bitmap,SKRect destination)
{ using var image=SKImage.FromBitmap(bitmap);canvas.DrawImage(image,destination,new SKSamplingOptions(SKCubicResampler.Mitchell)); }
void DrawRegion(SKCanvas canvas,SKBitmap bitmap,SKRect source,SKRect destination)
{ using var image=SKImage.FromBitmap(bitmap);canvas.DrawImage(image,source,destination,new SKSamplingOptions(SKCubicResampler.Mitchell)); }
void Save(SKBitmap bitmap,string path)
{using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);using var stream=File.Create(path);data.SaveTo(stream);}
void RenderSheet(string role,List<string> names)
{
    using var sheet=new SKBitmap(330*6,360*2);using var canvas=new SKCanvas(sheet);canvas.Clear(SKColor.Parse("#ece8df"));
    using var paint=new SKPaint{Color=SKColor.Parse("#273440"),IsAntialias=true};using var font=new SKFont(SKTypeface.Default,16);
    for(var i=0;i<names.Count;i++)
    {
        var x=i%6*330;var y=i/6*360;
        using var bitmap=Load(Path.Combine(output,names[i]));
        Draw(canvas,bitmap,new SKRect(x,y+25,x+330,y+355));
        canvas.DrawText(names[i].Replace("char_","").Replace(".png",""),x+12,y+21,SKTextAlign.Left,font,paint);
    }
    Save(sheet,Path.Combine(output,$"{role}-all-states.png"));
}
