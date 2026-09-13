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
    if (Environment.GetEnvironmentVariable("POLROB_INSPECTION") == "1")
        Save(cutBody, Path.Combine(output, $"{role}-cut-body.png"));
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
    using var headbandIdentityMask = NewBitmap();
    if (role == "robber")
    {
        // The beanie band and its two tapered end caps belong exclusively to
        // the fixed head. Restore that exact source region after side repair
        // so neither a donor torso nor a moving sleeve can extend it downward.
        using var headbandPath = ReferenceHeadbandPath();
        using (var canvas = new SKCanvas(headbandIdentityMask))
        {
            canvas.SetMatrix(registration);
            canvas.ClipPath(headbandPath, antialias: true);
            Draw(canvas, sourceIdentity, new SKRect(0, 0, 660, 660));
        }
        MergeIdentityMask(identityMask, headbandIdentityMask);
        Save(headbandIdentityMask, Path.Combine(output, "robber-headband-identity-mask.png"));
    }
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
        var phase = frame == 0 ? 0 : MathF.Sin((frame - 1) * MathF.PI / 4);
        if (MathF.Abs(phase) < .0001f)
        {
            // Idle and the two neutral points of the eight-frame cycle are
            // the registered source itself.  Re-compositing segmented pieces
            // here needlessly exposed tiny seams at the hand/waist joins.
            canvas.DrawBitmap(reference, 0, 0);
        }
        else
        {
            // The completed round torso is the back layer. Moving sleeves
            // cover it naturally; the protected central original is restored
            // below.
            canvas.DrawBitmap(body, 0, 0);
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
        }
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
    if (role == "police")
    {
        using var arrestProtectedFace = ReferenceFacePath("police");
        arrestProtectedFace.Transform(registration);
        SaveMask(arrestProtectedFace, Path.Combine(output, "police-arrest-protected-face-mask.png"));
        ComposeSpecial(specialLayer, body, specialName, true, reference, identityMask, arrestProtectedFace);
    }
    else ComposeSpecial(specialLayer, body, specialName, false, reference, identityMask, protectedHead);
    names.Add(specialName);
    if (role == "robber")
    {
        using var jailSource = Load(Path.Combine(work, "pose-reference", "robber_prison_break.png"));
        using var jailGuide = Load(Path.Combine(work, "pose-reference-masks", "robber_prison_break_arms.png"));
        // The generated guide identifies the parts, while a small expansion
        // lets the supplied source alpha—not the generated contour—define the
        // exact outside edge of the sleeves, cuffs, key, and lock.
        using var jailArms = ClipByGuide(jailSource, jailGuide, 20,
        [
            // Generated guide gaps at the two sleeve/hand joins.
            (323, 637, 333, 659),
            (674, 651, 695, 683),
            // Small missing pieces on the key shaft/teeth.
            (479, 775, 489, 779),
            (496, 790, 503, 801)
        ]);
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
        ComposeSpecial(jailLayer, body, jailName, true, reference, identityMask, jailProtectedFace,
            headbandIdentityMask);
        names.Add(jailName);
    }
    RenderSheet(role, new List<string> { $"{role}-reference.png" }.Concat(names).ToList());
}
File.WriteAllText(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }));
File.WriteAllText(Path.Combine(output, "registration.json"), JsonSerializer.Serialize(registrations, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Built and checked {audit.Count} normalized frames.");

void ComposeSpecial(SKBitmap arms, SKBitmap body, string name, bool armsCanCoverTorso, SKBitmap reference,
    SKBitmap identityMask, SKPath protectedHead, SKBitmap? exactIdentityOverlay = null)
{
    using var result = NewBitmap();
    using var canvas = new SKCanvas(result);
    if (!armsCanCoverTorso)
    {
        // Raised surrender arms sit behind the fixed torso. Drawing each
        // layer only once avoids accumulating alpha on the torso edge.
        canvas.DrawBitmap(arms, 0, 0);
        canvas.DrawBitmap(body, 0, 0);
        PreserveIdentity(result, reference, identityMask);
    }
    else
    {
        // Arrest/jailbreak limbs sit in front of the torso but behind the
        // protected face. Source-over the protection onto the already opaque
        // body+limb result: recomputing cover+arm alpha here used to erase the
        // underlying body at the antialiased clip boundary and create gaps.
        canvas.DrawBitmap(body, 0, 0);
        PreserveIdentity(result, reference, identityMask);
        canvas.DrawBitmap(arms, 0, 0);
        canvas.Save();
        canvas.ClipPath(protectedHead, antialias: true);
        canvas.DrawBitmap(body, 0, 0);
        canvas.Restore();
        PreserveIdentity(result, reference, identityMask, protectedHead);
    }
    if (exactIdentityOverlay is not null)
        PreserveIdentity(result, reference, exactIdentityOverlay);
    SaveAndValidate(result, name, armsCanCoverTorso, reference, identityMask, protectedHead);
}

SKBitmap ExtendBodySides(SKBitmap cutBody, string role)
{
    // The idle hands obscure a narrow strip of each waist. The supplied
    // special pose tells us only where the hidden outer silhouette continues.
    // Colors always come from the idle character; the donor never paints the
    // headband or torso. Continuous subpixel boundaries replace the old
    // per-row rounded cutoffs that looked jagged on transparent backgrounds.
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

    var riseStart = role == "police" ? 500 : 535;
    var riseEnd = role == "police" ? 565 : 575;
    var fallStart = role == "police" ? 685 : 705;
    // At these rows the original body path is fully exposed again. Ending
    // here preserves the source's natural lower belt/chest antialiasing.
    var fallEnd = role == "police" ? 755 : 770;

    var leftEdges = new float[size];
    var rightEdges = new float[size];
    var valid = new bool[size];
    for (var y = riseStart; y < fallEnd; y++)
    {
        var originalRow = RowBounds(cutBody, y, 128);
        var donorRow = RowBounds(donor, y, 128);
        if (originalRow is null || donorRow is null) continue;
        var (oldLeft, oldRight) = originalRow.Value;
        var (donorLeft, donorRight) = donorRow.Value;
        var reveal = Math.Min(
            SmoothStep(riseStart, riseEnd, y),
            1 - SmoothStep(fallStart, fallEnd, y));
        var oldLeftEdge = oldLeft + .5f;
        var oldRightEdge = oldRight - .5f;
        var donorLeftEdge = donorLeft + .5f;
        var donorRightEdge = donorRight - .5f;
        const float underlap = 6f;
        leftEdges[y] = oldLeftEdge + (Math.Min(oldLeftEdge, donorLeftEdge - underlap) - oldLeftEdge) * reveal;
        rightEdges[y] = oldRightEdge + (Math.Max(oldRightEdge, donorRightEdge + underlap) - oldRightEdge) * reveal;
        valid[y] = true;
    }

    SmoothBoundary(leftEdges, valid, riseStart, fallEnd, 3);
    SmoothBoundary(rightEdges, valid, riseStart, fallEnd, 3);
    EnforceRoundBoundary(leftEdges, valid, riseStart, fallEnd, leftSide: true);
    EnforceRoundBoundary(rightEdges, valid, riseStart, fallEnd, leftSide: false);
    // Monotonic enforcement can leave a one-row corner.  A final light pass
    // keeps the overall round direction while removing that last staircase.
    SmoothBoundary(leftEdges, valid, riseStart, fallEnd, 4);
    SmoothBoundary(rightEdges, valid, riseStart, fallEnd, 4);

    // Sample the idle artwork first, then smooth the side material vertically
    // as one profile.  Choosing a fresh six-pixel average independently for
    // every scanline produced the former fur-like horizontal banding.
    var leftOutline = new SKColor[size];
    var leftFill = new SKColor[size];
    var rightOutline = new SKColor[size];
    var rightFill = new SKColor[size];
    for (var y = riseStart; y < fallEnd; y++)
    {
        if (!valid[y]) continue;
        var originalRow = RowBounds(cutBody, y, 128);
        if (originalRow is null) continue;
        var (oldLeft, oldRight) = originalRow.Value;
        leftOutline[y] = FindSideMaterialColor(cutBody, oldLeft + .5f, y, 1, role, outline: true);
        leftFill[y] = FindSideMaterialColor(cutBody, oldLeft + .5f, y, 1, role, outline: false);
        rightOutline[y] = FindSideMaterialColor(cutBody, oldRight - .5f, y, -1, role, outline: true);
        rightFill[y] = FindSideMaterialColor(cutBody, oldRight - .5f, y, -1, role, outline: false);
    }
    SmoothColorProfile(leftOutline, valid, riseStart, fallEnd, 6);
    SmoothColorProfile(leftFill, valid, riseStart, fallEnd, 6);
    SmoothColorProfile(rightOutline, valid, riseStart, fallEnd, 6);
    SmoothColorProfile(rightFill, valid, riseStart, fallEnd, 6);

    for (var y = riseStart; y < fallEnd; y++)
    {
        if (!valid[y]) continue;
        var originalRow = RowBounds(cutBody, y, 128);
        if (originalRow is null) continue;
        var (oldLeft, oldRight) = originalRow.Value;
        if (role == "robber")
        {
            CompleteSideWarp(result, cutBody, y, leftEdges[y], oldLeft + .5f, leftSide: true);
            CompleteSideWarp(result, cutBody, y, rightEdges[y], oldRight - .5f, leftSide: false);
        }
        else
        {
            CompleteSide(result, y, leftEdges[y], oldLeft + .5f, leftOutline[y], leftFill[y], leftSide: true);
            CompleteSide(result, y, rightEdges[y], oldRight - .5f, rightOutline[y], rightFill[y], leftSide: false);
        }
    }
    return result;
}

void SmoothBoundary(float[] boundary, bool[] valid, int start, int end, int passes)
{
    int[] weights = [1, 4, 6, 4, 1];
    for (var pass = 0; pass < passes; pass++)
    {
        var previous = (float[])boundary.Clone();
        for (var y = start; y < end; y++)
        {
            if (!valid[y]) continue;
            var sum = 0f;
            var total = 0;
            for (var offset = -2; offset <= 2; offset++)
            {
                var sampleY = Math.Clamp(y + offset, start, end - 1);
                if (!valid[sampleY]) continue;
                var weight = weights[offset + 2];
                sum += previous[sampleY] * weight;
                total += weight;
            }
            if (total > 0) boundary[y] = sum / total;
        }
    }
}

void EnforceRoundBoundary(float[] boundary, bool[] valid, int start, int end, bool leftSide)
{
    var rows = Enumerable.Range(start, end - start).Where(y => valid[y]).ToArray();
    if (rows.Length == 0) return;
    var widest = leftSide
        ? rows.MinBy(y => boundary[y])
        : rows.MaxBy(y => boundary[y]);
    for (var y = start + 1; y <= widest; y++) if (valid[y] && valid[y - 1])
        boundary[y] = leftSide ? Math.Min(boundary[y], boundary[y - 1]) : Math.Max(boundary[y], boundary[y - 1]);
    for (var y = widest + 1; y < end; y++) if (valid[y] && valid[y - 1])
        boundary[y] = leftSide ? Math.Max(boundary[y], boundary[y - 1]) : Math.Min(boundary[y], boundary[y - 1]);
}

void SmoothColorProfile(SKColor[] colors, bool[] valid, int start, int end, int passes)
{
    int[] weights = [1, 4, 6, 4, 1];
    for (var pass = 0; pass < passes; pass++)
    {
        var previous = (SKColor[])colors.Clone();
        for (var y = start; y < end; y++)
        {
            if (!valid[y]) continue;
            var red = 0f; var green = 0f; var blue = 0f; var total = 0;
            for (var offset = -2; offset <= 2; offset++)
            {
                var sampleY = Math.Clamp(y + offset, start, end - 1);
                if (!valid[sampleY]) continue;
                var weight = weights[offset + 2];
                red += previous[sampleY].Red * weight;
                green += previous[sampleY].Green * weight;
                blue += previous[sampleY].Blue * weight;
                total += weight;
            }
            if (total > 0)
                colors[y] = new SKColor((byte)MathF.Round(red / total), (byte)MathF.Round(green / total),
                    (byte)MathF.Round(blue / total), 255);
        }
    }
}

void CompleteSideWarp(SKBitmap destination, SKBitmap source, int y, float completedEdge,
    float originalEdge, bool leftSide)
{
    if (leftSide && completedEdge >= originalEdge - .05f) return;
    if (!leftSide && completedEdge <= originalEdge + .05f) return;

    // The robber's dark torso is one continuous material at the arm seam.
    // Move the source edge to the completed edge and smoothly decay that
    // displacement over the next 40px.  The old black cut line becomes the
    // new outer outline, while every inner pixel comes from the original
    // texture and converges exactly back to its original coordinate.
    const float depth = 40;
    var innerEdge = originalEdge + (leftSide ? depth : -depth);
    var minimum = (int)MathF.Floor(Math.Min(completedEdge, innerEdge)) - 2;
    var maximum = (int)MathF.Ceiling(Math.Max(completedEdge, innerEdge)) + 2;
    var displacement = originalEdge - completedEdge;
    for (var x = Math.Max(0, minimum); x <= Math.Min(size - 1, maximum); x++)
    {
        var center = x + .5f;
        var t = leftSide
            ? (center - completedEdge) / (innerEdge - completedEdge)
            : (completedEdge - center) / (completedEdge - innerEdge);
        if (t < 0 || t > 1) continue;
        var eased = SmoothStep(0, 1, t);
        var sampleCenter = center + displacement * (1 - eased);
        var sampled = SampleHorizontalPremultiplied(source, sampleCenter - .5f, y);
        if (sampled.Alpha == 0) continue;
        var outerCoverage = leftSide
            ? SmoothStep(completedEdge - 1.1f, completedEdge + 1.1f, center)
            : 1 - SmoothStep(completedEdge - 1.1f, completedEdge + 1.1f, center);
        sampled = sampled.WithAlpha((byte)Math.Clamp(MathF.Round(sampled.Alpha * outerCoverage), 0, 255));
        destination.SetPixel(x, y, sampled);
    }
}

void CompleteSide(SKBitmap destination, int y, float completedEdge, float originalEdge,
    SKColor outlineColor, SKColor fillColor, bool leftSide)
{
    if (leftSide && completedEdge >= originalEdge - .05f) return;
    if (!leftSide && completedEdge <= originalEdge + .05f) return;

    // Only synthesize the pixels that were hidden by the idle hand.  The old
    // implementation also repainted a fixed 34px strip *inside* the original
    // body.  That strip appeared at full strength even when the extension was
    // less than one pixel, creating the conspicuous horizontal rectangles at
    // the beginning and end of the repaired area.
    const float seamDepth = 10;
    var innerEdge = originalEdge + (leftSide ? seamDepth : -seamDepth);
    var minimum = (int)MathF.Floor(Math.Min(completedEdge, innerEdge)) - 2;
    var maximum = (int)MathF.Ceiling(Math.Max(completedEdge, innerEdge)) + 2;
    var extension = MathF.Abs(originalEdge - completedEdge);
    var seamActivation = SmoothStep(.5f, 6, extension);
    for (var x = Math.Max(0, minimum); x <= Math.Min(size - 1, maximum); x++)
    {
        var center = x + .5f;
        var coverage = leftSide
            ? SmoothStep(completedEdge - .75f, completedEdge + .75f, center)
            : 1 - SmoothStep(completedEdge - .75f, completedEdge + .75f, center);
        var insideCompletedBody = leftSide ? center <= innerEdge : center >= innerEdge;
        if (!insideCompletedBody) continue;
        var baseColor = destination.GetPixel(x, y);

        var fillProgress = leftSide
            ? SmoothStep(completedEdge + 3, completedEdge + 15, center)
            : 1 - SmoothStep(completedEdge - 15, completedEdge - 3, center);
        var rebuiltColor = MixRgb(outlineColor, fillColor, fillProgress)
            .WithAlpha((byte)Math.Clamp(MathF.Round(coverage * 255), 0, 255));
        if (rebuiltColor.Alpha == 0) continue;
        var outsideOriginal = leftSide ? center <= originalEdge : center >= originalEdge;
        if (outsideOriginal)
        {
            // Existing source antialiasing stays on top of the new underlay.
            destination.SetPixel(x, y, SourceOver(baseColor, rebuiltColor));
            continue;
        }

        // Remove only the old arm/body cut line.  Blending ten pixels inward
        // is enough to turn that former outline into continuous torso
        // material without recreating the broad rectangular repaint that
        // caused the previous artifact.
        var baseAmount = leftSide
            ? SmoothStep(originalEdge, innerEdge, center)
            : 1 - SmoothStep(innerEdge, originalEdge, center);
        var repairAmount = seamActivation * (1 - baseAmount);
        if (repairAmount <= 0 || baseColor.Alpha == 0) continue;
        destination.SetPixel(x, y,
            MixRgb(baseColor, fillColor.WithAlpha(baseColor.Alpha), repairAmount));
    }
}

SKColor FindSideMaterialColor(SKBitmap image, float edge, int y, int inwardDirection,
    string role, bool outline)
{
    long red = 0, green = 0, blue = 0;
    var matches = 0;
    for (var verticalDistance = 0; verticalDistance <= 8 && matches < 6; verticalDistance++)
    {
        foreach (var sampleY in verticalDistance == 0
            ? new[] { y }
            : new[] { y - verticalDistance, y + verticalDistance })
        {
            if (sampleY < 0 || sampleY >= image.Height) continue;
            for (var distance = outline ? 0 : 5; distance <= 90 && matches < 6; distance++)
            {
                var sampleX = Math.Clamp((int)MathF.Round(edge) + distance * inwardDirection, 0, image.Width - 1);
                var color = image.GetPixel(sampleX, sampleY);
                if (color.Alpha < 180) continue;
                var minimum = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
                var maximum = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
                var luminance = (color.Red + color.Green + color.Blue) / 3;
                var matchesMaterial = outline
                    ? luminance <= 48
                    : role == "police"
                        ? color.Blue >= 58 && color.Blue > color.Red * 1.45f && color.Blue > color.Green * 1.12f
                        : luminance is >= 20 and <= 62 && maximum - minimum <= 24;
                if (!matchesMaterial) continue;
                red += color.Red;
                green += color.Green;
                blue += color.Blue;
                matches++;
            }
        }
    }
    if (matches > 0)
        return new SKColor((byte)(red / matches), (byte)(green / matches), (byte)(blue / matches), 255);
    var fallback = SampleHorizontalPremultiplied(image,
        edge + inwardDirection * (outline ? 2 : 24), y);
    return fallback.Alpha == 0 ? SKColors.Black : fallback.WithAlpha(255);
}

SKColor MixRgb(SKColor from, SKColor to, float amount)
{
    amount = Math.Clamp(amount, 0, 1);
    return new SKColor(
        (byte)MathF.Round(from.Red + (to.Red - from.Red) * amount),
        (byte)MathF.Round(from.Green + (to.Green - from.Green) * amount),
        (byte)MathF.Round(from.Blue + (to.Blue - from.Blue) * amount),
        from.Alpha);
}

SKColor SourceOver(SKColor foreground, SKColor background)
{
    var foregroundAlpha = foreground.Alpha / 255f;
    var backgroundAlpha = background.Alpha / 255f;
    var alpha = foregroundAlpha + backgroundAlpha * (1 - foregroundAlpha);
    if (alpha <= 0) return SKColors.Transparent;
    return new SKColor(
        (byte)MathF.Round((foreground.Red * foregroundAlpha + background.Red * backgroundAlpha * (1 - foregroundAlpha)) / alpha),
        (byte)MathF.Round((foreground.Green * foregroundAlpha + background.Green * backgroundAlpha * (1 - foregroundAlpha)) / alpha),
        (byte)MathF.Round((foreground.Blue * foregroundAlpha + background.Blue * backgroundAlpha * (1 - foregroundAlpha)) / alpha),
        (byte)MathF.Round(alpha * 255));
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

SKColor SampleHorizontalPremultiplied(SKBitmap image, float x, int y)
{
    var x0 = Math.Clamp((int)MathF.Floor(x), 0, image.Width - 1);
    var x1 = Math.Min(image.Width - 1, x0 + 1);
    var t = x - x0;
    var a = image.GetPixel(x0, y);
    var b = image.GetPixel(x1, y);
    var alphaA = a.Alpha / 255f;
    var alphaB = b.Alpha / 255f;
    var alpha = alphaA + (alphaB - alphaA) * t;
    if (alpha <= 0) return SKColors.Transparent;
    return new SKColor(
        (byte)MathF.Round((a.Red * alphaA * (1 - t) + b.Red * alphaB * t) / alpha),
        (byte)MathF.Round((a.Green * alphaA * (1 - t) + b.Green * alphaB * t) / alpha),
        (byte)MathF.Round((a.Blue * alphaA * (1 - t) + b.Blue * alphaB * t) / alpha),
        (byte)MathF.Round(alpha * 255));
}

(int Left, int Right)? RowBounds(SKBitmap image, int y, byte threshold)
{
    var left = image.Width;
    var right = -1;
    for (var x = 0; x < image.Width; x++)
        if (image.GetPixel(x, y).Alpha >= threshold) { left = Math.Min(left, x); right = x + 1; }
    return right < left ? null : (left, right);
}

SKBitmap ClipByGuide(SKBitmap source, SKBitmap guide, int expandGuidePixels = 0,
    IReadOnlyList<(int Left, int Top, int Right, int Bottom)>? filledGuidePatches = null)
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
    if (filledGuidePatches is not null)
    {
        foreach (var patch in filledGuidePatches)
        for (var y = Math.Max(0, patch.Top); y < Math.Min(source.Height, patch.Bottom); y++)
        for (var x = Math.Max(0, patch.Left); x < Math.Min(source.Width, patch.Right); x++)
            guideMask[y * source.Width + x] = true;
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
    path.MoveTo(0, 0); path.LineTo(660, 0); path.LineTo(660, 220);
    if (role == "police")
    {
        // Follow the actual hat-to-sleeve seam instead of cutting the source
        // on a horizontal y=255 line.  That cut became a little scissor-like
        // tab when the right sleeve rotated away in run frames 6–8.
        path.LineTo(607, 220);
        path.CubicTo(602, 234, 594, 247, 585, 255);
        path.CubicTo(614, 329, 595, 407, 561, 468);
        path.CubicTo(551, 484, 552, 492, 552, 500);
        path.CubicTo(552, 512, 547, 524, 544, 536);
    }
    else
    {
        path.LineTo(600, 220);
        path.CubicTo(594, 235, 584, 248, 578, 255);
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
        path.CubicTo(66, 247, 58, 234, 53, 220);
    }
    else
    {
        path.CubicTo(119, 526, 116, 516, 116, 505);
        path.CubicTo(116, 499, 116, 487, 105, 470);
        path.CubicTo(65, 390, 66, 324, 82, 255);
        path.CubicTo(76, 248, 66, 235, 60, 220);
    }
    path.LineTo(0, 220); path.Close();
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

SKPath ReferenceHeadbandPath()
{
    // Central beanie + band envelope. Its lower corners taper inward before
    // the sleeves begin, so it cannot become a vertical bridge to the gray
    // chest stripe when the side torso is reconstructed.
    var path = new SKPath();
    path.MoveTo(60, -20);
    path.LineTo(600, -20);
    path.LineTo(600, 220);
    path.CubicTo(600, 262, 582, 292, 555, 310);
    path.LineTo(105, 310);
    path.CubicTo(78, 292, 60, 262, 60, 220);
    path.Close();
    return path;
}

void MergeIdentityMask(SKBitmap destination, SKBitmap source)
{
    for (var y = 0; y < destination.Height; y++)
    for (var x = 0; x < destination.Width; x++)
        if (source.GetPixel(x, y).Alpha >= 128)
            destination.SetPixel(x, y, SKColors.White);
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
    // Keep a small hidden underlap for the maximum 6-degree arm swing.  A
    // wider strip pulled the torso's dark outline out from beneath the hand
    // when the arm rotated, leaving a little hook/spike at the waist join.
    using var insetBody = new SKPath(bodyPath);
    insetBody.Transform(SKMatrix.CreateTranslation(side < 0 ? 6 : -6, 0));
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
    TrimArmWaistHook(arm, side, role);
    return arm;
}

void TrimArmWaistHook(SKBitmap arm, int side, string role)
{
    // In the idle source a few near-black waist-outline pixels bridge the
    // inside tip of each hand to the torso. They are invisible while the
    // pieces are stationary, but become a downward hook when the arm swings.
    // Fade only those dark bridge pixels; the warm hand outline and sleeve
    // artwork remain untouched, and the completed torso is already behind it.
    var startY = role == "police" ? 504 : 508;
    var endY = role == "police" ? 517 : 521;
    var innerStart = role == "police" ? 92 : 101;
    for (var y = startY; y <= endY; y++)
    for (var x = 0; x < arm.Width; x++)
    {
        var localX = side < 0 ? x : arm.Width - 1 - x;
        if (localX < innerStart) continue;
        var color = arm.GetPixel(x, y);
        if (color.Alpha == 0 || Math.Max(color.Red, Math.Max(color.Green, color.Blue)) >= 110) continue;
        var keep = 1 - SmoothStep(startY, endY, y);
        arm.SetPixel(x, y, color.WithAlpha((byte)MathF.Round(color.Alpha * keep)));
    }
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
    if (Environment.GetEnvironmentVariable("POLROB_INSPECTION") == "1")
    {
        var inspectionDirectory = Path.Combine(output, "inspection");
        Directory.CreateDirectory(inspectionDirectory);
        using var inspection = NewBitmap();
        using var inspectionCanvas = new SKCanvas(inspection);
        inspectionCanvas.Clear(SKColor.Parse("#ece8df"));
        inspectionCanvas.DrawBitmap(sprite, 0, 0);
        Save(inspection, Path.Combine(inspectionDirectory, name));
    }
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
