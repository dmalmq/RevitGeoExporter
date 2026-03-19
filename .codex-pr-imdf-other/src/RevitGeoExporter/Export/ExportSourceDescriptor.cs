using System;
using Autodesk.Revit.DB;

namespace RevitGeoExporter.Export;

public sealed class ExportSourceDescriptor
{
    public ExportSourceDescriptor(
        ProjectLocation projectionProjectLocation,
        Transform transformToHost,
        bool isLinkedSource,
        long? linkInstanceId = null,
        string? linkInstanceName = null)
    {
        ProjectionProjectLocation = projectionProjectLocation ?? throw new ArgumentNullException(nameof(projectionProjectLocation));
        TransformToHost = transformToHost ?? throw new ArgumentNullException(nameof(transformToHost));
        IsLinkedSource = isLinkedSource;
        LinkInstanceId = linkInstanceId;
        LinkInstanceName = string.IsNullOrWhiteSpace(linkInstanceName) ? null : linkInstanceName.Trim();
    }

    public ProjectLocation ProjectionProjectLocation { get; }

    public Transform TransformToHost { get; }

    public bool IsLinkedSource { get; }

    public long? LinkInstanceId { get; }

    public string? LinkInstanceName { get; }

    public static ExportSourceDescriptor CreateHost(Document hostDocument)
    {
        if (hostDocument is null)
        {
            throw new ArgumentNullException(nameof(hostDocument));
        }

        if (hostDocument.ActiveProjectLocation == null)
        {
            throw new InvalidOperationException("The host document must have an active project location.");
        }

        return new ExportSourceDescriptor(hostDocument.ActiveProjectLocation, Transform.Identity, isLinkedSource: false);
    }

    public static ExportSourceDescriptor CreateLinked(Document hostDocument, LinkedViewSourceContext linkedSource)
    {
        if (hostDocument is null)
        {
            throw new ArgumentNullException(nameof(hostDocument));
        }

        if (linkedSource is null)
        {
            throw new ArgumentNullException(nameof(linkedSource));
        }

        if (hostDocument.ActiveProjectLocation == null)
        {
            throw new InvalidOperationException("The host document must have an active project location.");
        }

        return new ExportSourceDescriptor(
            hostDocument.ActiveProjectLocation,
            linkedSource.TransformToHost,
            isLinkedSource: true,
            linkedSource.LinkInstance.Id.Value,
            linkedSource.LinkInstance.Name);
    }
}
