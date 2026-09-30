using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Engraving.DisplayLists;
using Tessitura.Smufl;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;

namespace Tessitura.Engraving;

/// <summary>Contains one system composed as an A4 display-list page.</summary>
/// <param name="Page">The immutable page primitives in staff-space coordinates.</param>
/// <param name="StaffSpacePoints">The page-point size of one staff space.</param>
/// <param name="SystemIndex">The zero-based system index in the score.</param>
/// <param name="MeasureRange">The consecutive measures displayed by the system.</param>
public sealed record ScorePageComposition(
    Page Page,
    double StaffSpacePoints,
    int SystemIndex,
    SystemLineMeasureRange MeasureRange,
    DisplayPoint? CursorLocation);

/// <summary>Describes an exact musical cursor position for page composition.</summary>
/// <param name="StaffIndex">The zero-based staff index.</param>
/// <param name="Position">The exact score position in whole-note units.</param>
public readonly record struct EngravingCursor(int StaffIndex, Fraction Position);

/// <summary>Composes score events and layout into a page suitable for display.</summary>
public sealed class ScorePageComposer
{
    private const double PageWidthPoints = 595;
    private const double PageHeightPoints = 842;
    private const double HorizontalMarginPoints = 36;
    private const double VerticalMarginPoints = 36;
    private const double StaffHeightSpaces = 4;
    private const double StaffGapSpaces = 2.5;
    private const double RequestedStaffSpacePoints = 12;
    private readonly SmuflMetadata _metadata;
    private readonly Style _style;
    private readonly HorizontalSpacer _horizontalSpacer = new();

    /// <summary>Creates a score-page composer for one music font and engraving style.</summary>
    /// <param name="metadata">The selected SMuFL font metrics and glyph map.</param>
    /// <param name="style">The current engraving style.</param>
    public ScorePageComposer(SmuflMetadata metadata, Style style)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _style = style ?? throw new ArgumentNullException(nameof(style));
    }

    /// <summary>Gets the staff-space size that fits all staves on an A4 page.</summary>
    /// <param name="score">The immutable score snapshot.</param>
    /// <returns>The staff-space size in page points.</returns>
    public double GetStaffSpacePoints(Score score)
    {
        ArgumentNullException.ThrowIfNull(score);
        int staffCount = CountStaves(score);
        if (staffCount == 0)
        {
            throw new ArgumentException("A score must contain at least one staff.", nameof(score));
        }

        double staffPitch = StaffHeightSpaces + StaffGapSpaces;
        double maximumStaffSpace = (PageHeightPoints - 2 * VerticalMarginPoints) /
            (staffCount * staffPitch + StaffGapSpaces);
        return Math.Min(RequestedStaffSpacePoints, maximumStaffSpace);
    }

    /// <summary>Gets the music width available to the system breaker for the score.</summary>
    /// <param name="score">The immutable score snapshot.</param>
    /// <returns>The system's usable width in staff spaces after its header.</returns>
    public double GetAvailableWidth(Score score)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (score.Measures.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A score must contain at least one measure.", nameof(score));
        }

        double staffSpace = GetStaffSpacePoints(score);
        double headerWidth = 0;
        int headerStaffCount = CountStaves(score);
        foreach (Measure measure in score.Measures)
        {
            for (int staffIndex = 0; staffIndex < headerStaffCount; staffIndex++)
            {
                double currentWidth = _horizontalSpacer.BuildHeader(_metadata,
                    ClefGlyphName(GetStaffClef(score, staffIndex)),
                    measure.KeySignature, measure.TimeSignature, _style).MusicStartX;
                headerWidth = Math.Max(headerWidth, currentWidth);
            }
        }

        double pageWidth = PageWidthPoints / staffSpace;
        double margin = HorizontalMarginPoints / staffSpace;
        double availableWidth = pageWidth - 2 * margin - headerWidth;
        if (availableWidth <= 0)
        {
            throw new InvalidOperationException("The score header leaves no room for music on the page.");
        }

        return availableWidth;
    }

    /// <summary>Composes the system containing one measure into an A4 page.</summary>
    /// <param name="score">The immutable score snapshot.</param>
    /// <param name="layout">The measured and system-broken score.</param>
    /// <param name="measureIndex">The measure whose system should be composed.</param>
    /// <param name="cursor">The optional musical cursor to place on the page.</param>
    /// <param name="cancellationToken">Cancels composition without returning partial primitives.</param>
    /// <returns>The display page and its score-system location.</returns>
    public ScorePageComposition Compose(Score score, ScoreLayoutResult layout, int measureIndex,
        EngravingCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(layout);
        cancellationToken.ThrowIfCancellationRequested();
        if (measureIndex < 0 || measureIndex >= score.Measures.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(measureIndex));
        }

        int systemIndex = FindSystem(layout.Systems, measureIndex);
        SystemLine system = layout.Systems[systemIndex];
        double staffSpace = GetStaffSpacePoints(score);
        double pageWidth = PageWidthPoints / staffSpace;
        double pageHeight = PageHeightPoints / staffSpace;
        double leftMargin = HorizontalMarginPoints / staffSpace;
        double topMargin = VerticalMarginPoints / staffSpace;
        TimeSignature firstMeter = score.Measures[system.Range.StartIndex].TimeSignature;
        int staffCount = CountStaves(score);
        SystemHeaderLayout header = BuildSystemHeader(score, system.Range.StartIndex, staffCount);
        ImmutableArray<DrawingPrimitive>.Builder primitives = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        StaffElementPlacer placer = new(_metadata, _style);
        EventId staffLineId = new(Guid.Empty);
        VerticalLayoutResult verticalLayout = BuildVerticalLayout(layout.Systems,
            staffCount, pageHeight, topMargin);
        int pageNumber = verticalLayout.Systems[systemIndex].PageNumber;

        for (int pageSystemIndex = 0; pageSystemIndex < layout.Systems.Length; pageSystemIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SystemVerticalPlacement placement = verticalLayout.Systems[pageSystemIndex];
            if (placement.PageNumber != pageNumber)
            {
                continue;
            }

            SystemLine pageSystem = layout.Systems[pageSystemIndex];
            ImmutableArray<double> measureWidths = GetDisplayMeasureWidths(score, layout, pageSystem);
            SystemHeaderLayout pageHeader = BuildSystemHeader(score, pageSystem.Range.StartIndex, staffCount);
            double musicStartX = leftMargin + pageHeader.MusicStartX;
            double systemWidth = Sum(measureWidths);
            double musicEndX = musicStartX + systemWidth;
            for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double staffTop = placement.StaffTops[staffIndex];
                primitives.AddRange(placer.PlaceStaffLines(staffLineId, leftMargin,
                    musicEndX, staffTop));
                Clef staffClef = GetStaffClef(score, staffIndex);
                SystemHeaderLayout staffHeader = _horizontalSpacer.BuildHeader(_metadata,
                    ClefGlyphName(staffClef), score.Measures[pageSystem.Range.StartIndex].KeySignature,
                    score.Measures[pageSystem.Range.StartIndex].TimeSignature, _style);
                AddHeader(primitives, staffHeader, leftMargin, staffTop, staffClef,
                    score.Measures[pageSystem.Range.StartIndex].KeySignature);
                AddBarline(primitives, staffLineId,
                    musicStartX - _style.MinimumRhythmicGap, staffTop,
                    _style.StaffLineThickness);

                double measureStartX = musicStartX;
                for (int localMeasure = 0; localMeasure < pageSystem.Range.Count; localMeasure++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int scoreMeasureIndex = pageSystem.Range.StartIndex + localMeasure;
                    double measureWidth = measureWidths[localMeasure];
                    if (score.Content.TryGetValue(new StaffMeasureKey(staffIndex, scoreMeasureIndex),
                        out StaffMeasure? staffMeasure))
                    {
                        AddStaffMeasure(primitives, placer, score, staffMeasure,
                            scoreMeasureIndex, staffClef, measureStartX, measureWidth, staffTop,
                            cancellationToken);
                    }

                    measureStartX += measureWidth;
                    AddBarline(primitives, staffLineId, measureStartX, staffTop,
                        _style.StaffLineThickness);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        Page page = new(pageNumber, pageWidth, pageHeight, primitives.ToImmutable());
        DisplayPoint? cursorLocation = cursor is EngravingCursor inputCursor
            ? LocateCursor(score, system, verticalLayout.Systems[systemIndex], inputCursor,
                leftMargin + header.MusicStartX,
                GetDisplayMeasureWidths(score, layout, system))
            : null;
        return new ScorePageComposition(page, staffSpace, systemIndex, system.Range, cursorLocation);
    }

    private SystemHeaderLayout BuildSystemHeader(Score score, int measureIndex, int staffCount)
    {
        // All staves in a system share one music start, so use the widest header.
        SystemHeaderLayout widest = default!;
        for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
        {
            SystemHeaderLayout current = _horizontalSpacer.BuildHeader(_metadata,
                ClefGlyphName(GetStaffClef(score, staffIndex)),
                score.Measures[measureIndex].KeySignature,
                score.Measures[measureIndex].TimeSignature, _style);
            if (widest is null || current.MusicStartX > widest.MusicStartX)
            {
                widest = current;
            }
        }

        return widest;
    }

    private static Clef GetStaffClef(Score score, int staffIndex)
    {
        int remaining = staffIndex;
        foreach (Instrument instrument in score.Instruments)
        {
            if (remaining < instrument.Staves.Length)
            {
                return instrument.Staves[remaining].InitialClef;
            }

            remaining -= instrument.Staves.Length;
        }

        return Clef.Treble;
    }

    private static string ClefGlyphName(Clef clef) => clef switch
    {
        Clef.Treble => "gClef",
        Clef.Bass => "fClef",
        Clef.Alto or Clef.Tenor => "cClef",
        _ => throw new ArgumentOutOfRangeException(nameof(clef)),
    };

    private void AddHeader(ImmutableArray<DrawingPrimitive>.Builder primitives,
        SystemHeaderLayout header, double leftMargin, double staffTop, Clef clef,
        KeySignature key)
    {
        int keyIndex = 0;
        foreach (HeaderSymbol symbol in header.Symbols)
        {
            double y = symbol.Part switch
            {
                // SMuFL clef origins sit on the line each clef names.
                HeaderPart.Clef => clef switch
                {
                    Clef.Treble => staffTop + 3.5,
                    Clef.Bass => staffTop + 1,
                    Clef.Alto => staffTop + 2,
                    _ => staffTop + 1,
                },
                HeaderPart.KeySignature =>
                    staffTop + 4 - KeySignaturePositions.Get(key.Fifths, keyIndex++, clef) * 0.5,
                HeaderPart.MeterNumerator => staffTop + 1.6,
                HeaderPart.MeterDenominator => staffTop + 3.6,
                _ => staffTop + 1.5,
            };
            AddGlyph(primitives, new ElementId(Guid.Empty), symbol.GlyphName,
                leftMargin + symbol.X, y);
        }
    }

    private void AddStaffMeasure(ImmutableArray<DrawingPrimitive>.Builder primitives,
        StaffElementPlacer placer, Score score, StaffMeasure staffMeasure, int measureIndex,
        Clef clef, double measureStartX, double measureWidth, double staffTop,
        CancellationToken cancellationToken)
    {
        AccidentalMark[] marks = ResolveAccidentals(score.Measures[measureIndex], measureIndex, staffMeasure,
            out (int Voice, EventId Event, int Note)[] order);
        bool manyVoices = staffMeasure.Voices.Length > 1;
        double headWidth = _metadata.GetBoundingBox("noteheadBlack").NorthEast.X;
        double barLength = (double)score.Measures[measureIndex].TimeSignature.Length.Num /
            score.Measures[measureIndex].TimeSignature.Length.Den;
        foreach (Voice voice in staffMeasure.Voices)
        {
            // Behind Bars, Multiple Voices: odd voices take up-stems, even voices down-stems.
            StemDirection? voiceStem = manyVoices
                ? (voice.Number % 2 == 1 ? StemDirection.Up : StemDirection.Down)
                : null;
            double restShift = !manyVoices ? 0 : voice.Number switch { 1 => -2, 2 => 2, 3 => -4, _ => 4 };
            cancellationToken.ThrowIfCancellationRequested();
            foreach (MusicEvent topLevel in voice.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (topLevel is TupletGroup group)
                {
                    AddTuplet(primitives, placer, group, voice, staffMeasure, clef, measureStartX, measureWidth,
                        staffTop, barLength, order, marks, manyVoices, headWidth, voiceStem, restShift);
                    continue;
                }

                AddLeaf(primitives, placer, topLevel, topLevel.Onset, voice, staffMeasure, clef, measureStartX, measureWidth,
                    staffTop, barLength, order, marks, manyVoices, headWidth, voiceStem, restShift);
            }
        }
    }

    private void AddLeaf(ImmutableArray<DrawingPrimitive>.Builder primitives, StaffElementPlacer placer, MusicEvent leaf,
        Fraction position, Voice voice, StaffMeasure staffMeasure, Clef clef, double measureStartX, double measureWidth,
        double staffTop, double barLength, (int Voice, EventId Event, int Note)[] order, AccidentalMark[] marks,
        bool manyVoices, double headWidth, StemDirection? voiceStem, double restShift)
    {
        double x = measureStartX + measureWidth * ((double)position.Num / position.Den) / barLength;
        if (leaf is Rest rest)
        {
            primitives.AddRange(placer.PlaceRest(rest.Id, rest.Duration, x, staffTop, restShift));
            return;
        }

        Chord chord = (Chord)leaf;
        for (int noteIndex = 0; noteIndex < chord.Notes.Length; noteIndex++)
        {
            Note note = chord.Notes[noteIndex];
            AccidentalMark accidental = AccidentalMark.None;
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i].Voice == voice.Number && order[i].Event == chord.Id && order[i].Note == noteIndex)
                {
                    accidental = marks[i];
                    break;
                }
            }

            double chordOffset = chord.Notes.Length <= 1 ? 0 : noteIndex * 0.75;
            if (manyVoices && CollidesWithLowerVoice(staffMeasure, voice, chord, note, clef))
            {
                chordOffset += headWidth; // Behind Bars, Multiple Voices: displace the clashing head
            }

            primitives.AddRange(placer.PlaceNote(chord.Id, note.Pitch,
                chord.Duration, accidental, x + chordOffset, staffTop, clef, voiceStem));
        }
    }

    // Draws the members at their sounding onsets, then the bracket and number above the group.
    // Behind Bars, Tuplets: a bracket with the ratio number spans the group unless the notes are beamed.
    private void AddTuplet(ImmutableArray<DrawingPrimitive>.Builder primitives, StaffElementPlacer placer, TupletGroup group,
        Voice voice, StaffMeasure staffMeasure, Clef clef, double measureStartX, double measureWidth, double staffTop,
        double barLength, (int Voice, EventId Event, int Note)[] order, AccidentalMark[] marks, bool manyVoices,
        double headWidth, StemDirection? voiceStem, double restShift)
    {
        double left = double.MaxValue;
        double right = double.MinValue;
        foreach ((MusicEvent leaf, Fraction onset, _) in new[] { (MusicEvent)group }.Flatten())
        {
            AddLeaf(primitives, placer, leaf, onset, voice, staffMeasure, clef, measureStartX, measureWidth, staffTop,
                barLength, order, marks, manyVoices, headWidth, voiceStem, restShift);
            double x = measureStartX + measureWidth * ((double)onset.Num / onset.Den) / barLength;
            left = Math.Min(left, x);
            right = Math.Max(right, x + 1.2);
        }

        bool below = voiceStem == StemDirection.Down;
        double y = below ? staffTop + 6 : staffTop - 2;
        double hook = below ? -0.8 : 0.8;
        ElementId id = new(group.Id.Value);
        string number = group.Actual.ToString(System.Globalization.CultureInfo.InvariantCulture);
        double numberWidth = 0;
        foreach (char digit in number)
        {
            numberWidth += _metadata.GetBoundingBox($"tuplet{digit}").NorthEast.X;
        }

        double middle = (left + right) / 2;
        double gapLeft = middle - numberWidth / 2 - 0.4;
        double gapRight = middle + numberWidth / 2 + 0.4;
        double thickness = _style.StaffLineThickness * 1.5;
        primitives.Add(BracketLine(id, left, y, gapLeft, y, thickness));
        primitives.Add(BracketLine(id, gapRight, y, right, y, thickness));
        primitives.Add(BracketLine(id, left, y, left, y + hook, thickness));
        primitives.Add(BracketLine(id, right, y, right, y + hook, thickness));
        double digitX = middle - numberWidth / 2;
        foreach (char digit in number)
        {
            string name = $"tuplet{digit}";
            AddGlyph(primitives, id, name, digitX, y + (below ? 0.5 : 0.5));
            digitX += _metadata.GetBoundingBox(name).NorthEast.X;
        }
    }

    private static DisplayLine BracketLine(ElementId id, double x1, double y1, double x2, double y2, double thickness) =>
        new(id, new DisplayBox(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1) + thickness, Math.Abs(y2 - y1) + thickness),
            new DisplayPoint(x1, y1), new DisplayPoint(x2, y2), thickness);

    // A head of a higher-numbered voice clashes when a lower voice has a head within a step at the same
    // onset; a unison of equal note values shares one head instead.
    private static bool CollidesWithLowerVoice(StaffMeasure measure, Voice voice, Chord chord, Note note, Clef clef)
    {
        int position = StaffPitchPosition.Get(note.Pitch, clef);
        foreach (Voice other in measure.Voices)
        {
            if (other.Number >= voice.Number)
            {
                continue;
            }

            foreach (MusicEvent musicEvent in other.Events)
            {
                if (musicEvent is not Chord otherChord || musicEvent.Onset != chord.Onset)
                {
                    continue;
                }

                foreach (Note otherNote in otherChord.Notes)
                {
                    int distance = Math.Abs(StaffPitchPosition.Get(otherNote.Pitch, clef) - position);
                    bool sameHead = distance == 0 && HeadKind(otherChord.Duration) == HeadKind(chord.Duration);
                    if (distance <= 1 && !sameHead)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static int HeadKind(Duration duration) => duration.Value switch
    {
        NoteValue.Whole => 0,
        NoteValue.Half => 1,
        _ => 2,
    };

    private static AccidentalMark[] ResolveAccidentals(Measure measure, int measureIndex,
        StaffMeasure staffMeasure, out (int Voice, EventId Event, int Note)[] order)
    {
        // Accidentals follow the sounding order of the measure across voices
        // (Behind Bars, Accidentals and Key Signatures > Using accidentals).
        List<(int Voice, Chord Chord, Fraction Onset, Chord? Previous)> chords = [];
        foreach (Voice voice in staffMeasure.Voices)
        {
            Chord? previous = null;
            foreach ((MusicEvent leaf, Fraction onset, _) in voice.Events.Flatten())
            {
                if (leaf is Chord chord)
                {
                    chords.Add((voice.Number, chord, onset, previous));
                    previous = chord;
                }
                else
                {
                    previous = null;
                }
            }
        }

        chords.Sort((a, b) => a.Onset.CompareTo(b.Onset));
        int count = 0;
        foreach ((int _, Chord chord, Fraction _, Chord? _) in chords)
        {
            count += chord.Notes.Length;
        }

        order = new (int, EventId, int)[count];
        AccidentalInput[] inputs = new AccidentalInput[count];
        int slot = 0;
        foreach ((int voice, Chord chord, _, Chord? previous) in chords)
        {
            for (int noteIndex = 0; noteIndex < chord.Notes.Length; noteIndex++)
            {
                Pitch pitch = chord.Notes[noteIndex].Pitch;
                bool tiedFromPrevious = false;
                if (previous is not null)
                {
                    foreach (Note candidate in previous.Notes)
                    {
                        if (candidate.TiedToNext && candidate.Pitch == pitch)
                        {
                            tiedFromPrevious = true;
                        }
                    }
                }

                order[slot] = (voice, chord.Id, noteIndex);
                inputs[slot] = new AccidentalInput(measureIndex, pitch, tiedFromPrevious, measure.KeySignature);
                slot++;
            }
        }

        ImmutableArray<AccidentalMark> resolved = new AccidentalResolver().Resolve(inputs);
        AccidentalMark[] marks = new AccidentalMark[count];
        resolved.CopyTo(marks);
        return marks;
    }

    private void AddGlyph(ImmutableArray<DrawingPrimitive>.Builder primitives,
        ElementId elementId, string glyphName, double x, double y)
    {
        SmuflBoundingBox box = _metadata.GetBoundingBox(glyphName);
        DisplayBox bounds = new(x + box.SouthWest.X, y - box.NorthEast.Y,
            box.NorthEast.X - box.SouthWest.X,
            box.NorthEast.Y - box.SouthWest.Y);
        primitives.Add(new DisplayGlyph(elementId, bounds,
            _metadata.GetGlyphCodepoint(glyphName), new DisplayPoint(x, y), 4));
    }

    private static void AddBarline(ImmutableArray<DrawingPrimitive>.Builder primitives,
        EventId id, double x, double staffTop, double thickness)
    {
        double top = staffTop;
        double bottom = staffTop + StaffHeightSpaces;
        primitives.Add(new DisplayLine(new ElementId(id.Value),
            new DisplayBox(x - thickness / 2, top, thickness, bottom - top),
            new DisplayPoint(x, top), new DisplayPoint(x, bottom), thickness));
    }

    private static int CountStaves(Score score)
    {
        int count = 0;
        foreach (Instrument instrument in score.Instruments)
        {
            count = checked(count + instrument.Staves.Length);
        }

        return count;
    }

    private static int FindSystem(ImmutableArray<SystemLine> systems, int measureIndex)
    {
        for (int index = 0; index < systems.Length; index++)
        {
            SystemLineMeasureRange range = systems[index].Range;
            if (measureIndex >= range.StartIndex && measureIndex < range.StartIndex + range.Count)
            {
                return index;
            }
        }

        throw new InvalidOperationException("The score layout does not contain the requested measure.");
    }

    private static DisplayPoint? LocateCursor(Score score, SystemLine system,
        SystemVerticalPlacement placement, EngravingCursor cursor, double musicStartX,
        ImmutableArray<double> measureWidths)
    {
        if (cursor.StaffIndex < 0 || cursor.StaffIndex >= CountStaves(score) ||
            cursor.Position < Fraction.Zero)
        {
            return null;
        }

        Fraction measureStart = Fraction.Zero;
        int cursorMeasureIndex = score.Measures.Length - 1;
        Fraction localPosition = Fraction.Zero;
        for (int index = 0; index < score.Measures.Length; index++)
        {
            Fraction measureLength = score.Measures[index].TimeSignature.Length;
            Fraction measureEnd = measureStart + measureLength;
            if (cursor.Position < measureEnd || index == score.Measures.Length - 1)
            {
                cursorMeasureIndex = index;
                localPosition = cursor.Position - measureStart;
                if (localPosition < Fraction.Zero)
                {
                    localPosition = Fraction.Zero;
                }
                else if (localPosition > measureLength)
                {
                    localPosition = measureLength;
                }

                break;
            }

            measureStart = measureEnd;
        }

        if (cursorMeasureIndex < system.Range.StartIndex ||
            cursorMeasureIndex >= system.Range.StartIndex + system.Range.Count)
        {
            return null;
        }

        int localMeasureIndex = cursorMeasureIndex - system.Range.StartIndex;
        double x = musicStartX;
        for (int index = 0; index < localMeasureIndex; index++)
        {
            x += measureWidths[index];
        }

        Fraction currentMeasureLength = score.Measures[cursorMeasureIndex].TimeSignature.Length;
        double position = (double)localPosition.Num / localPosition.Den;
        double measureLengthValue = (double)currentMeasureLength.Num / currentMeasureLength.Den;
        x += measureWidths[localMeasureIndex] * position / measureLengthValue;
        double y = placement.StaffTops[cursor.StaffIndex] + 2;
        return new DisplayPoint(x, y);
    }

    private ImmutableArray<double> GetDisplayMeasureWidths(Score score, ScoreLayoutResult layout,
        SystemLine system)
    {
        if (layout.Systems.Length != 1 || system.NaturalWidth >= GetAvailableWidth(score))
        {
            return system.MeasureWidths;
        }

        double elasticity = 0;
        for (int index = 0; index < system.Range.Count; index++)
        {
            elasticity += layout.MeasureWidths[system.Range.StartIndex + index].Elasticity;
        }

        if (elasticity <= 0)
        {
            return system.MeasureWidths;
        }

        double additionalWidth = GetAvailableWidth(score) - system.NaturalWidth;
        ImmutableArray<double>.Builder widths =
            ImmutableArray.CreateBuilder<double>(system.Range.Count);
        for (int index = 0; index < system.Range.Count; index++)
        {
            double measureElasticity = layout.MeasureWidths[system.Range.StartIndex + index].Elasticity;
            widths.Add(system.MeasureWidths[index] + additionalWidth * measureElasticity / elasticity);
        }

        return widths.MoveToImmutable();
    }

    private static double Sum(ImmutableArray<double> values)
    {
        double total = 0;
        for (int index = 0; index < values.Length; index++)
        {
            total += values[index];
        }

        return total;
    }

    private static VerticalLayoutResult BuildVerticalLayout(ImmutableArray<SystemLine> systems,
        int staffCount, double pageHeight, double pageMargin)
    {
        ImmutableArray<StaffSkyline>.Builder staves = ImmutableArray.CreateBuilder<StaffSkyline>(staffCount);
        for (int index = 0; index < staffCount; index++)
        {
            staves.Add(new StaffSkyline(0.5, 0.5));
        }

        VerticalSystem verticalSystem = new(staves.MoveToImmutable(),
            MinimumStaffGap: 1.5,
            SkylineClearance: 1,
            LeadingSpace: 1,
            TrailingSpace: 1);
        VerticalSystem[] verticalSystems = new VerticalSystem[systems.Length];
        Array.Fill(verticalSystems, verticalSystem);
        return new VerticalPageLayouter().Layout(verticalSystems, pageHeight,
            pageMargin, pageMargin, systemGap: 2);
    }
}
