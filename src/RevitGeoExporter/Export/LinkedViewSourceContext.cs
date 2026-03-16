using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace RevitGeoExporter.Export;

public sealed class LinkedViewSourceContext
{
    public LinkedViewSourceContext(
        RevitLinkInstance linkInstance,
        Document linkedDocument,
        Transform transformToHost,
        string sourceDocumentKey,
        string sourceDocumentName,
        IReadOnlyList<Floor> floors,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<Stairs> stairs,
        IReadOnlyList<FamilyInstance> familyUnits,
        IReadOnlyList<FamilyInstance> openings,
        IReadOnlyList<FamilyInstance> unsupportedOpenings,
        IReadOnlyList<CurveElement> detailCurves)
    {
        LinkInstance = linkInstance ?? throw new ArgumentNullException(nameof(linkInstance));
        LinkedDocument = linkedDocument ?? throw new ArgumentNullException(nameof(linkedDocument));
        TransformToHost = transformToHost ?? throw new ArgumentNullException(nameof(transformToHost));
        SourceDocumentKey = string.IsNullOrWhiteSpace(sourceDocumentKey)
            ? throw new ArgumentException("A source document key is required.", nameof(sourceDocumentKey))
            : sourceDocumentKey.Trim();
        SourceDocumentName = string.IsNullOrWhiteSpace(sourceDocumentName)
            ? throw new ArgumentException("A source document name is required.", nameof(sourceDocumentName))
            : sourceDocumentName.Trim();
        Floors = floors ?? throw new ArgumentNullException(nameof(floors));
        Rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
        Stairs = stairs ?? throw new ArgumentNullException(nameof(stairs));
        FamilyUnits = familyUnits ?? throw new ArgumentNullException(nameof(familyUnits));
        Openings = openings ?? throw new ArgumentNullException(nameof(openings));
        UnsupportedOpenings = unsupportedOpenings ?? throw new ArgumentNullException(nameof(unsupportedOpenings));
        DetailCurves = detailCurves ?? throw new ArgumentNullException(nameof(detailCurves));
    }

    public RevitLinkInstance LinkInstance { get; }

    public Document LinkedDocument { get; }

    public Transform TransformToHost { get; }

    public string SourceDocumentKey { get; }

    public string SourceDocumentName { get; }

    public IReadOnlyList<Floor> Floors { get; }

    public IReadOnlyList<Room> Rooms { get; }

    public IReadOnlyList<Stairs> Stairs { get; }

    public IReadOnlyList<FamilyInstance> FamilyUnits { get; }

    public IReadOnlyList<FamilyInstance> Openings { get; }

    public IReadOnlyList<FamilyInstance> UnsupportedOpenings { get; }

    public IReadOnlyList<CurveElement> DetailCurves { get; }
}
