namespace SchemaStudioWebViewer.WEBSemanticModel.Model
{
    public enum SourceKind
    {
        NamedObject,
        DerivedQuery,
        Function
    }

    public enum ColumnKind
    {
        Simple,
        Aggregate,
        Window,
        Expression
    }
}

