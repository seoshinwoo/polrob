using SkiaSharp;
using System.Text.Json;
using System.Security.Cryptography;

if (args.Length != 2) throw new ArgumentException("Usage: <character-animation-docs> <output-directory>");
var work = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (new[] { "source", "generated", "reference", "reference-masks", "pose-reference", "pose-reference-masks" }
    .Any(folder => output == Path.Combine(work, folder)))
    throw new ArgumentException("Output must not overwrite source artwork or generated layers.");
Directory.CreateDirectory(output);
// The body stays centered at a stable pivot. The slightly larger common
// canvas lets the reference jailbreak lock keep its natural distance below
// the hands without shrinking the character or lowering the shoulders.
const int size = 1088;
const float pivot = 544;
const float bodyWidth = 512;
var audit = new List<object>();
var registrations = new List<object>();
foreach (var role in new[] { "police", "robber" })
{
    // Identity comes from the user's exact 660px artwork, never from a
    // regenerated torso. The fitted segmentation retains the original neck
    // and shoulders; only the detached sleeve/hand pixels are articulated.
    using var source = Load(Path.Combine(work, "reference", $"{role}.png"));
    if (source.Width != 660 || source.Height != 660) throw new InvalidOperationException("Reference registration requires 660px originals.");
    using var bodyPath = ReferenceBodyPath(role);
    using var isolated = Clip(source, bodyPath, false);
    var bounds = Bounds(isolated);
    var scale = bodyWidth / bounds.Width;
    var target = SKRect.Create(pivot - bodyWidth / 2, pivot - bounds.Height * scale / 2, bodyWidth, bounds.Height * scale);
    using var cutBody = NewBitmap();
    using (var canvas = new SKCanvas(cutBody)) DrawRegion(canvas, isolated, bounds, target);
    using var body = ExtendBodySides(cutBody, role);
    Save(body, Path.Combine(output, $"{role}-body.png"));
    var registration = SKMatrix.CreateScaleTranslation(scale, scale, pivot - bounds.MidX * scale, pivot - bounds.MidY * scale);
    registrations.Add(new { role, source = $"reference/{role}.png", sourceWidth = source.Width, sourceHeight = source.Height,
        sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(work, "reference", $"{role}.png")))),
        sourceBodyBounds = new[] { bounds.Left, bounds.Top, bounds.Right, bounds.Bottom }, scale,
        normalizedBodyBounds = new[] { target.Left, target.Top, target.Right, target.Bottom }, bodyWidth, pivot = new[] { pivot, pivot } });
    using var reference = NewBitmap();
    using (var canvas = new SKCanvas(reference))
    {
        canvas.SetMatrix(registration);
        Draw(canvas, source, new SKRect(0, 0, 660, 660));
    }
    Save(reference, Path.Combine(output, $"{role}-reference.png"));
    using var leftArm = ReferenceArm(source, bodyPath, -1, role);
    using var rightArm = ReferenceArm(source, bodyPath, 1, role);
    using var protectedHead = ReferenceHeadPath(role);
    protectedHead.Transform(registration);
    using var identityMask = NewBitmap();
    using var sourceIdentity = IdentitySilhouette(isolated);
    using (var canvas = new SKCanvas(identityMask))
    {
        canvas.SetMatrix(registration);
        Draw(canvas, sourceIdentity, new SKRect(0, 0, 660, 660));
    }
    ExcludeReconstructedPixels(identityMask, cutBody, body);
    Save(identityMask, Path.Combine(output, $"{role}-identity-mask.png"));
    using var headIdentityMask = NewBitmap();
    for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        if (identityMask.GetPixel(x, y).Alpha == 255 && Protected(protectedHead, x, y))
            headIdentityMask.SetPixel(x, y, SKColors.White);
    Save(headIdentityMask, Path.Combine(output, $"{role}-protected-head-mask.png"));
    var names = new List<string>();
    for (var frame = 0; frame <= 8; frame++)
    {
        var name = frame == 0 ? $"char_{role}.png" : $"char_{role}_run_{frame}.png";
        using var result = NewBitmap();
        using var canvas = new SKCanvas(result);
        // The completed round torso is the back layer. Moving sleeves cover
        // it naturally; the protected central original is restored below.
        canvas.DrawBitmap(body, 0, 0);
        var phase = frame == 0 ? 0 : MathF.Sin((frame - 1) * MathF.PI / 4);
        for (var side = -1; side <= 1; side += 2)
        {
            canvas.Save();
            canvas.SetMatrix(registration);
            var shoulderX = side < 0 ? 90 : 570;
            canvas.Translate(shoulderX, 290);
            canvas.RotateDegrees(phase * 6);
            canvas.Scale(1, 1 + phase * side * .10f);
            canvas.Translate(-shoulderX, -290);
            Draw(canvas, side < 0 ? leftArm : rightArm, new SKRect(0, 0, 660, 660));
            canvas.Restore();
        }
        PreserveIdentity(result, reference, identityMask);
        SaveAndValidate(result, name, false, reference, identityMask, protectedHead);
        names.Add(name);
    }

    var special = role == "police" ? "arrest" : "surrend";
    using var specialSource = Load(Path.Combine(work, "pose-reference", $"{role}_{special}.png"));
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
            canvas.Translate(pivot - 512 * specialScale, target.Top - 154 * specialScale);
            canvas.Scale(specialScale);
        }
        else canvas.Translate(pivot - 512, target.Top - 190);
        if (role == "police") Draw(canvas, specialArms, new SKRect(0, 0, 1024, 1024));
        else
        {
            DrawRegion(canvas, specialArms, new SKRect(0,0,512,1024), new SKRect(25,0,537,1024));
            DrawRegion(canvas, specialArms, new SKRect(512,0,1024,1024), new SKRect(487,0,999,1024));
        }
    }
    var specialName = $"char_{role}_{special}.png";
    ComposeSpecial(specialLayer, body, specialName, role == "police", reference, identityMask, protectedHead);
    names.Add(specialName);
    if (role == "robber")
    {
        using var jailSource = Load(Path.Combine(work, "pose-reference", "robber_prison_break.png"));
        using var jailGuide = Load(Path.Combine(work, "pose-reference-masks", "robber_prison_break_arms.png"));
        // The generated guide identifies the parts, while a small expansion
        // lets the supplied source alpha—not the generated contour—define the
        // exact outside edge of the sleeves, cuffs, key, and lock.
        using var jailArms = ClipByGuide(jailSource, jailGuide, 20);
        RemoveSmallComponents(jailArms, 32, 100);
        using var jailLayer = NewBitmap();
        using (var canvas = new SKCanvas(jailLayer))
        {
            // Registration measured from the identical hat/eye landmarks in
            // the supplied idle and jailbreak references. Added canvas inset
            // keeps the shared body pivot at the center of the 1088px canvas.
            canvas.Translate(33.85f, 140.04f);
            canvas.Scale(1.00028f);
            Draw(canvas, jailArms, new SKRect(0, 0, 1024, 1024));
        }
        using var jailProtectedFace = ReferenceFacePath("robber");
        jailProtectedFace.Transform(registration);
        SaveMask(jailProtectedFace, Path.Combine(output, "robber-jail-protected-face-mask.png"));
        const string jailName = "char_robber_prison_break.png";
        ComposeSpecial(jailLayer, body, jailName, true, reference, identityMask, jailProtectedFace);
        names.Add(jailName);
    }
    RenderSheet(role, new List<string> { $"{role}-reference.png" }.Concat(names).ToList());
}
File.WriteAllText(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }));
File.WriteAllText(Path.Combine(output, "registration.json"), JsonSerializer.Serialize(registrations, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Built and checked {audit.Count} normalized frames.");

void ComposeSpecial(SKBitmap arms, SKBitmap body, string name, bool armsCanCoverTorso, SKBitmap reference, SKBitmap identityMask, SKPath protectedHead)
{
    using var result = NewBitmap();
    using var canvas = new SKCanvas(result);
    canvas.DrawBitmap(body, 0, 0);
    PreserveIdentity(result, reference, identityMask);
    // Paint limbs once. Reinsert the original body through its coverage
    // mask wherever limbs must pass behind it, so soft edges do not gain
    // opacity from drawing the same arm twice.
    canvas.DrawBitmap(arms, 0, 0);
    using var occlusion = NewBitmap();
    using (var maskCanvas = new SKCanvas(occlusion))
    {
        if (armsCanCoverTorso) maskCanvas.ClipPath(protectedHead, antialias: true);
        maskCanvas.DrawBitmap(body, 0, 0);
    }
    for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
    {
        var cover = occlusion.GetPixel(x, y);
        if (cover.Alpha == 0) continue;
        var arm = arms.GetPixel(x, y);
        // Reference-over-arm premultiplied alpha, independent of the
        // provisional lower-torso foreground composition above.
        var a = cover.Alpha / 255f; var b = arm.Alpha / 255f * (1 - a);
        var total = a + b;
        spritePixel(x, y, cover, arm, a, b, total);
    }
    PreserveIdentity(result, reference, identityMask, armsCanCoverTorso ? protectedHead : null);
    SaveAndValidate(result, name, armsCanCoverTorso, reference, identityMask, protectedHead);

    void spritePixel(int x, int y, SKColor cover, SKColor arm, float a, float b, float total)
    {
        result.SetPixel(x, y, new SKColor(
            (byte)Math.Clamp(MathF.Round((cover.Red * a + arm.Red * b) / total), 0, 255),
            (byte)Math.Clamp(MathF.Round((cover.Green * a + arm.Green * b) / total), 0, 255),
            (byte)Math.Clamp(MathF.Round((cover.Blue * a + arm.Blue * b) / total), 0, 255),
            (byte)Math.Clamp(MathF.Round(total * 255), 0, 255)));
    }
}

SKBitmap ExtendBodySides(SKBitmap cutBody, string role)
{
    // The idle hands obscure a narrow strip of each waist. The supplied
    // special pose tells us only where the hidden outer silhouette continues.
    // Stretch pixels from the idle torso itself into that silhouette so no
    // differently lit donor artwork or interior sleeve outline can appear.
    var pose = role == "police" ? "police_arrest" : "robber_surrend";
    using var donorSource = Load(Path.Combine(work, "pose-reference", $"{pose}.png"));
    using var donorGuide = Load(Path.Combine(work, "pose-reference-masks", $"{pose}_body.png"));
    using var donorCutout = ClipByGuide(donorSource, donorGuide);
    using var donor = NewBitmap();
    using (var canvas = new SKCanvas(donor))
    {
        if (role == "police")
        {
            // Side-waist registration follows the exposed outer curve. Its
            // vertical proportion differs slightly from the idle head.
            canvas.Translate(32, 144);
            canvas.Scale(1, .94f);
        }
        else
        {
            // Surrender reference -> normalized idle registration, widened
            // by 5% only across the hidden side strips for a round waist.
            canvas.Translate(31, 57.2f);
            canvas.Scale(1.017f, .9686f);
        }
        Draw(canvas, donorCutout, new SKRect(0, 0, 1024, 1024));
    }
    var result = NewBitmap();
    using (var canvas = new SKCanvas(result)) canvas.DrawBitmap(cutBody, 0, 0);
    const int transitionDepth = 34;
    const int riseStart = 500;
    const int riseEnd = 570;
    const int fallStart = 720;
    const int fallEnd = 790;
    for (var y = riseStart; y < fallEnd; y++)
    {
        var originalRow = RowBounds(cutBody, y, 32);
        var donorRow = RowBounds(donor, y, 32);
        if (originalRow is null || donorRow is null) continue;
        var (oldLeft, oldRight) = originalRow.Value;
        var (donorLeft, donorRight) = donorRow.Value;
        var reveal = Math.Min(
            SmoothStep(riseStart, riseEnd, y),
            1 - SmoothStep(fallStart, fallEnd, y));
        var desiredLeft = (int)MathF.Round(oldLeft + (Math.Min(oldLeft, donorLeft) - oldLeft) * reveal);
        var desiredRight = (int)MathF.Round(oldRight + (Math.Max(oldRight, donorRight) - oldRight) * reveal);

        // Remap a narrow strip instead of simply placing pixels behind the
        // old edge. This moves the former hand seam to the true outer contour
        // and blends back into untouched idle artwork without a doubled line.
        var leftInner = Math.Min(size, oldLeft + transitionDepth);
        var leftDenominator = Math.Max(1, leftInner - desiredLeft);
        for (var x = Math.Max(0, desiredLeft); x < leftInner; x++)
        {
            var sourceX = oldLeft + (x - desiredLeft) * transitionDepth / (float)leftDenominator;
            result.SetPixel(x, y, SampleHorizontal(cutBody, sourceX, y));
        }

        var rightInner = Math.Max(0, oldRight - transitionDepth);
        var rightDenominator = Math.Max(1, desiredRight - rightInner);
        for (var x = rightInner; x < Math.Min(size, desiredRight); x++)
        {
            var sourceX = rightInner + (x - rightInner) * transitionDepth / (float)rightDenominator;
            result.SetPixel(x, y, SampleHorizontal(cutBody, sourceX, y));
        }
    }

    if (role == "robber")
    {
        // The idle source contains sleeve-colored pixels immediately inside
        // the hidden shoulder seam. Blend only the RGB from the supplied
        // surrender torso into the already-completed body. Keeping the
        // completed body's alpha unchanged prevents donor-mask wisps or
        // rectangular tabs from altering the clean outer silhouette.
        const int sideDepth = 78;
        const int horizontalBlend = 24;
        const int verticalStart = 470;
        const int verticalFull = 540;
        const int verticalFall = 720;
        const int verticalEnd = 800;
        for (var y = verticalStart; y < verticalEnd; y++)
        {
            var originalRow = RowBounds(cutBody, y, 32);
            if (originalRow is null) continue;
            var (oldLeft, oldRight) = originalRow.Value;
            var verticalWeight = Math.Min(
                SmoothStep(verticalStart, verticalFull, y),
                1 - SmoothStep(verticalFall, verticalEnd, y));

            var leftInner = Math.Min(size, oldLeft + sideDepth);
            for (var x = Math.Max(0, oldLeft - transitionDepth); x < leftInner; x++)
            {
                var baseColor = result.GetPixel(x, y);
                var donorColor = donor.GetPixel(x, y);
                if (baseColor.Alpha == 0 || donorColor.Alpha == 0) continue;
                var horizontalWeight = 1 - SmoothStep(leftInner - horizontalBlend, leftInner, x);
                result.SetPixel(x, y, MixRgbKeepingAlpha(baseColor, donorColor, verticalWeight * horizontalWeight));
            }

            var rightInner = Math.Max(0, oldRight - sideDepth);
            for (var x = rightInner; x < Math.Min(size, oldRight + transitionDepth); x++)
            {
                var baseColor = result.GetPixel(x, y);
                var donorColor = donor.GetPixel(x, y);
                if (baseColor.Alpha == 0 || donorColor.Alpha == 0) continue;
                var horizontalWeight = SmoothStep(rightInner, rightInner + horizontalBlend, x);
                result.SetPixel(x, y, MixRgbKeepingAlpha(baseColor, donorColor, verticalWeight * horizontalWeight));
            }
        }
    }
    return result;
}

SKColor MixRgbKeepingAlpha(SKColor original, SKColor replacement, float amount)
{
    amount = Math.Clamp(amount, 0, 1);
    return new SKColor(
        (byte)MathF.Round(original.Red + (replacement.Red - original.Red) * amount),
        (byte)MathF.Round(original.Green + (replacement.Green - original.Green) * amount),
        (byte)MathF.Round(original.Blue + (replacement.Blue - original.Blue) * amount),
        original.Alpha);
}

void ExcludeReconstructedPixels(SKBitmap identityMask, SKBitmap originalBody, SKBitmap completedBody)
{
    var changed = new bool[size * size];
    for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        changed[y * size + x] = originalBody.GetPixel(x, y) != completedBody.GetPixel(x, y);
    for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
    {
        var reconstructed = false;
        for (var dy = -2; dy <= 2 && !reconstructed; dy++)
        for (var dx = -2; dx <= 2; dx++)
        {
            var nx = x + dx; var ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < size && ny < size && changed[ny * size + nx])
            { reconstructed = true; break; }
        }
        if (reconstructed) identityMask.SetPixel(x, y, SKColors.Transparent);
    }
}

float SmoothStep(float edge0, float edge1, float value)
{
    var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
    return t * t * (3 - 2 * t);
}

SKColor SampleHorizontal(SKBitmap image, float x, int y)
{
    var x0 = Math.Clamp((int)MathF.Floor(x), 0, image.Width - 1);
    var x1 = Math.Min(image.Width - 1, x0 + 1);
    var t = x - x0;
    var a = image.GetPixel(x0, y);
    var b = image.GetPixel(x1, y);
    return new SKColor(
        (byte)MathF.Round(a.Red + (b.Red - a.Red) * t),
        (byte)MathF.Round(a.Green + (b.Green - a.Green) * t),
        (byte)MathF.Round(a.Blue + (b.Blue - a.Blue) * t),
        (byte)MathF.Round(a.Alpha + (b.Alpha - a.Alpha) * t));
}

(int Left, int Right)? RowBounds(SKBitmap image, int y, byte threshold)
{
    var left = image.Width;
    var right = -1;
    for (var x = 0; x < image.Width; x++)
        if (image.GetPixel(x, y).Alpha >= threshold) { left = Math.Min(left, x); right = x + 1; }
    return right < left ? null : (left, right);
}

SKBitmap ClipByGuide(SKBitmap source, SKBitmap guide, int expandGuidePixels = 0)
{
    using var registeredGuide = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(registeredGuide))
        DrawRegion(canvas, guide, new SKRect(0, 0, guide.Width, guide.Height), new SKRect(0, 0, source.Width, source.Height));

    var guideMask = new bool[source.Width * source.Height];
    for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++)
    {
        var g = registeredGuide.GetPixel(x, y);
        var luminance = (g.Red + g.Green + g.Blue) / 3;
        guideMask[y * source.Width + x] = luminance >= 128;
    }
    if (expandGuidePixels > 0)
    {
        // Recover only the outward-facing row edges. A general dilation would
        // cross the inner shoulder seam and accidentally copy torso pixels.
        for (var y = 0; y < source.Height; y++)
        {
            var left = source.Width;
            var right = -1;
            for (var x = 0; x < source.Width; x++)
                if (guideMask[y * source.Width + x]) { left = Math.Min(left, x); right = x; }
            if (right < left) continue;
            for (var x = Math.Max(0, left - expandGuidePixels); x < left; x++)
                guideMask[y * source.Width + x] = true;
            for (var x = right + 1; x <= Math.Min(source.Width - 1, right + expandGuidePixels); x++)
                guideMask[y * source.Width + x] = true;
        }
    }
    var solid = new bool[source.Width * source.Height];
    for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++)
        solid[y * source.Width + x] = source.GetPixel(x, y).Alpha >= 240 && guideMask[y * source.Width + x];
    var coverage = Smooth(solid, source.Width, source.Height);
    var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
    for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++)
    {
        var alpha = (byte)Math.Clamp((coverage[y * source.Width + x] - .15f) / .7f * 255, 0, 255);
        if (alpha == 0) { result.SetPixel(x, y, SKColors.Transparent); continue; }
        var color = source.GetPixel(x, y);
        if (color.Alpha < 240)
        {
            var found = false;
            for (var radius = 1; radius <= 4 && !found; radius++)
            for (var dy = -radius; dy <= radius && !found; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                var nx = x + dx; var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= source.Width || ny >= source.Height || !solid[ny * source.Width + nx]) continue;
                color = source.GetPixel(nx, ny); found = true; break;
            }
        }
        result.SetPixel(x, y, color.WithAlpha(alpha));
    }
    return result;
}

void RemoveSmallComponents(SKBitmap image, byte threshold, int minimumPixels)
{
    var seen = new bool[image.Width * image.Height];
    var components = new List<List<int>>();
    for (var i = 0; i < seen.Length; i++)
    {
        if (seen[i] || image.GetPixel(i % image.Width, i / image.Width).Alpha < threshold) continue;
        var component = new List<int> { i };
        seen[i] = true;
        for (var j = 0; j < component.Count; j++) foreach (var neighbor in Neighbors(component[j], image.Width, image.Height))
        {
            if (seen[neighbor] || image.GetPixel(neighbor % image.Width, neighbor / image.Width).Alpha < threshold) continue;
            seen[neighbor] = true;
            component.Add(neighbor);
        }
        components.Add(component);
    }
    var keep = new bool[seen.Length];
    foreach (var component in components.Where(component => component.Count >= minimumPixels))
        foreach (var pixel in component) keep[pixel] = true;
    if (!keep.Any(value => value)) throw new InvalidOperationException("No substantial visible component found.");
    // Retain the antialiased fringe belonging to each substantial part while
    // dropping tiny glow/speck components from the transparent source.
    for (var pass = 0; pass < 3; pass++)
    {
        var next = (bool[])keep.Clone();
        for (var i = 0; i < keep.Length; i++) if (keep[i])
            foreach (var neighbor in Neighbors(i, image.Width, image.Height)) next[neighbor] = true;
        keep = next;
    }
    for (var i = 0; i < keep.Length; i++)
        if (!keep[i]) image.SetPixel(i % image.Width, i / image.Width, SKColors.Transparent);
}

SKPath ReferenceBodyPath(string role)
{
    // ImageGen's coarse masks are kept for provenance. Their silhouette was
    // fitted to the source's visible sleeve seam, because generated masks
    // can drift and must not change the source neck/shoulder geometry.
    var path = new SKPath();
    path.MoveTo(0, 0); path.LineTo(660, 0); path.LineTo(660, 255);
    if (role == "police")
    {
        path.LineTo(585, 255);
        path.CubicTo(614, 329, 595, 407, 561, 468);
        path.CubicTo(551, 484, 552, 492, 552, 500);
        path.CubicTo(552, 512, 547, 524, 544, 536);
    }
    else
    {
        path.LineTo(578, 255);
        path.CubicTo(594, 324, 595, 390, 555, 470);
        path.CubicTo(544, 487, 544, 499, 544, 505);
        path.CubicTo(544, 516, 541, 526, 538, 536);
    }
    path.LineTo(660, 536); path.LineTo(660, 660); path.LineTo(0, 660); path.LineTo(0, 536);
    path.LineTo(role == "police" ? 116 : 122, 536);
    if (role == "police")
    {
        path.CubicTo(113, 524, 108, 512, 108, 500);
        path.CubicTo(108, 492, 109, 484, 99, 468);
        path.CubicTo(65, 407, 46, 329, 75, 255);
    }
    else
    {
        path.CubicTo(119, 526, 116, 516, 116, 505);
        path.CubicTo(116, 499, 116, 487, 105, 470);
        path.CubicTo(65, 390, 66, 324, 82, 255);
    }
    path.LineTo(0, 255); path.Close();
    return path;
}

SKPath ReferenceHeadPath(string role)
{
    // Lock the original face AND neck/shoulder band. Forward hands are
    // allowed to pass only below it, not redraw the collar as a new shape.
    var path = new SKPath();
    path.MoveTo(-660, -660); path.LineTo(1320, -660); path.LineTo(1320, 500);
    path.LineTo(554, 500);
    path.CubicTo(510, 571, 450, 601, 330, role == "police" ? 598 : 607);
    path.CubicTo(210, 601, 150, 571, 106, 500);
    path.LineTo(-660, 500); path.Close();
    return path;
}

SKPath ReferenceFacePath(string role)
{
    var path = new SKPath();
    path.MoveTo(-660, -660); path.LineTo(1320, -660); path.LineTo(1320, 290);
    path.LineTo(role == "police" ? 565 : 558, 290);
    path.CubicTo(570, 425, 485, 525, 330, role == "police" ? 542 : 535);
    path.CubicTo(175, 525, 90, 425, role == "police" ? 95 : 102, 290);
    path.LineTo(-660, 290); path.Close();
    return path;
}

void SaveMask(SKPath path, string destination)
{
    using var mask = NewBitmap();
    using var canvas = new SKCanvas(mask);
    using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
    canvas.DrawPath(path, paint);
    Save(mask, destination);
}

SKBitmap ReferenceArm(SKBitmap source, SKPath bodyPath, int side, string role)
{
    using var region = new SKPath();
    region.AddRect(side < 0 ? new SKRect(0, 255, 330, 550) : new SKRect(330, 255, 660, 550));
    // Keep enough hidden underlap for the maximum 6-degree arm swing.
    using var insetBody = new SKPath(bodyPath);
    insetBody.Transform(SKMatrix.CreateTranslation(side < 0 ? 15 : -15, 0));
    using var armPath = region.Op(insetBody, SKPathOp.Difference) ?? throw new InvalidOperationException("Arm segmentation failed");
    using var handEnd = new SKPath();
    handEnd.MoveTo(0, 255); handEnd.LineTo(330, 255); handEnd.LineTo(330, 450);
    handEnd.LineTo(role == "police" ? 100 : 108, 450);
    if (role == "police")
    {
        handEnd.CubicTo(100, 465, 109, 478, 108, 500);
        handEnd.CubicTo(106, 512, 99, 519, 88, 523);
    }
    else
    {
        handEnd.CubicTo(108, 469, 117, 483, 115, 505);
        handEnd.CubicTo(113, 518, 105, 524, 89, 528);
    }
    handEnd.LineTo(0, 530); handEnd.Close();
    if (side > 0) handEnd.Transform(SKMatrix.CreateScale(-1, 1, 330, 0));
    using var trimmed = armPath.Op(handEnd, SKPathOp.Intersect) ?? throw new InvalidOperationException("Hand outline failed");
    var arm = Clip(source, trimmed, false);
    if (role == "robber") FeatherArmRoot(arm, 255, 18);
    return arm;
}

void FeatherArmRoot(SKBitmap arm, int top, int depth)
{
    // The source split begins on a horizontal crop edge hidden under the
    // idle torso. Fade that tiny underlap into the completed torso so rotating
    // it cannot expose a rectangular shoulder cap.
    for (var y = top; y < Math.Min(arm.Height, top + depth); y++)
    {
        var factor = SmoothStep(top, top + depth - 1, y);
        for (var x = 0; x < arm.Width; x++)
        {
            var color = arm.GetPixel(x, y);
            if (color.Alpha == 0) continue;
            arm.SetPixel(x, y, color.WithAlpha((byte)MathF.Round(color.Alpha * factor)));
        }
    }
}

SKBitmap IdentitySilhouette(SKBitmap isolated)
{
    var w = isolated.Width; var h = isolated.Height;
    var outside = new bool[w * h];
    var queue = new Queue<int>();
    for (var i = 0; i < outside.Length; i++)
        if ((i < w || i >= outside.Length - w || i % w == 0 || i % w == w - 1) && isolated.GetPixel(i % w, i / w).Alpha < 32)
        { outside[i] = true; queue.Enqueue(i); }
    while (queue.TryDequeue(out var i)) foreach (var n in Neighbors(i, w, h))
        if (!outside[n] && isolated.GetPixel(n % w, n / w).Alpha < 32)
        { outside[n] = true; queue.Enqueue(n); }
    var mask = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
    for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        mask.SetPixel(x, y, outside[y * w + x] ? SKColors.Transparent : SKColors.White);
    return mask;
}

bool Protected(SKPath path, int x, int y) => path.Contains(x - 3, y - 3) && path.Contains(x + 3, y + 3)
    && path.Contains(x - 3, y + 3) && path.Contains(x + 3, y - 3);

void PreserveIdentity(SKBitmap sprite, SKBitmap reference, SKBitmap mask, SKPath? head = null)
{
    // Copy registered original pixels, including semitransparent outlines
    // and transparent eyes. This avoids accumulating alpha by repainting a
    // reference whose interior alpha is often 253/254 rather than 255.
    for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        if (mask.GetPixel(x, y).Alpha == 255 && (head is null || Protected(head, x, y))) sprite.SetPixel(x, y, reference.GetPixel(x, y));
}

void Union(SKPath target,SKPath shape)
{
    using var combined=target.Op(shape,SKPathOp.Union)??throw new InvalidOperationException("Path union failed");
    target.Reset();target.AddPath(combined);
}

void SaveAndValidate(SKBitmap sprite, string name, bool armsCanCoverTorso, SKBitmap original, SKBitmap identityMask, SKPath protectedHead)
{
    var changed = 0;
    var protectedPixels = 0;
    for (var y = 0; y < size; y++)
    for (var x = 0; x < size; x++)
    {
        var reference = original.GetPixel(x, y);
        // Forward limbs may cover the torso. The central head/face must
        // remain identical, including all idle/run pixels across the body.
        var protectedPixel = !armsCanCoverTorso || Protected(protectedHead, x, y);
        if (identityMask.GetPixel(x, y).Alpha == 255 && protectedPixel)
        {
            protectedPixels++;
            if (sprite.GetPixel(x, y) != reference) changed++;
        }
    }
    if (changed != 0) throw new InvalidOperationException($"Body changed in {name}: {changed}");
    var bounds = Bounds(sprite);
    if (bounds.Left < 8 || bounds.Top < 8 || bounds.Right > size - 8 || bounds.Bottom > size - 8)
        throw new InvalidOperationException($"Clipped pose: {name} {bounds}");
    Save(sprite, Path.Combine(output, name));
    var topology = CountComponents(sprite);
    if (topology.Count != 1) throw new InvalidOperationException($"Detached artwork in {name}: {string.Join(",", topology)}");
    audit.Add(new { name, width = size, height = size, bodyWidth, pivot = new[] {pivot,pivot},
        comparisonRegion = !armsCanCoverTorso ? "registered original body RGBA, excluding reconstructed side strips" : "registered original protected head/face RGBA, excluding foreground arms",
        protectedPixels, changedProtectedPixels = changed, connectedComponentsAtAlpha32 = topology.Count,
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
