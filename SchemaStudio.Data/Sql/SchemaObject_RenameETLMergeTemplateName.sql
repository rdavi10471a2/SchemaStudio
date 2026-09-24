-- Rename dbo.SchemaObject.ETLMergeTemplateName -> ETLMergeProcedureName.
-- Idempotent: only renames when the old column still exists and the new one does not,
-- so it is safe to run repeatedly and against databases that were already migrated.
IF COL_LENGTH('dbo.SchemaObject', 'ETLMergeTemplateName') IS NOT NULL
   AND COL_LENGTH('dbo.SchemaObject', 'ETLMergeProcedureName') IS NULL
BEGIN
    EXEC sp_rename
        @objname = N'dbo.SchemaObject.ETLMergeTemplateName',
        @newname = N'ETLMergeProcedureName',
        @objtype = N'COLUMN';
END;
