using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.Components.Pages.ObjectShaper;

[FileVersion("1.0")]
[AIFileContext(
    "Components/Pages/ObjectShaper/ObjectShapePresentation.cs",
    "Presentation helpers for Object Shaper row states.",
    Responsibilities = "Translate field and join state into compact labels and CSS class names while leaving selection rules in ObjectShapeSession.",
    Nuances = "This class exists to keep the Razor page mostly declarative and prevent business selection rules from leaking into markup.")]
public static class ObjectShapePresentation
{
    public static string FieldRoleClass(ObjectShapeField field)
    {
        return field.Role switch
        {
            "Lookup value" => "os-badge os-badge--lookup",
            "Expression" => "os-badge os-badge--expression",
            _ => "os-badge"
        };
    }

    public static string JoinRowClass(ObjectShapeJoinRow row)
    {
        return row.IsActive
            ? "os-join-row os-join-row--active"
            : "os-join-row os-join-row--inactive";
    }

    public static string JoinStatusClass(ObjectShapeJoinRow row)
    {
        if (row.IsRequiredByWhere)
        {
            return "os-pill os-pill--where";
        }

        return row.IsActive
            ? "os-pill os-pill--active"
            : "os-pill os-pill--inactive";
    }
}
