/* This project came from a fork of: https://github.com/aspnet/EntityFrameworkCore
 * Copyright (c) .NET Foundation. All rights reserved.
 * Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
 * 
 *          Copyright (c)  2018 Rafael Almeida (ralms@ralms.net)
 *
 *                    Ralms.EntityFrameworkCore.Oracle
 *
 * THIS MATERIAL IS PROVIDED AS IS, WITH ABSOLUTELY NO WARRANTY EXPRESSED
 * OR IMPLIED.  ANY USE IS AT YOUR OWN RISK.
 *
 * Permission is hereby granted to use or copy this program
 * for any purpose,  provided the above notices are retained on all copies.
 * Permission to modify the code and to distribute modified code is granted,
 * provided the above notices are retained, and a notice that the code was
 * modified is included with the above copyright notice.
 *
 */

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Ralms.EntityFrameworkCore.Oracle.Metadata.Internal;
using System;
using System.Linq;
using System.Text;

namespace Microsoft.EntityFrameworkCore.Migrations
{
    public class OracleMigrationsSqlGenerator : MigrationsSqlGenerator
    {
        public OracleMigrationsSqlGenerator(
            MigrationsSqlGeneratorDependencies dependencies,
            IMigrationsAnnotationProvider migrationsAnnotations)
            : base(dependencies)
        {
        }

        protected override void ColumnDefinition(AddColumnOperation operation, IModel model, MigrationCommandListBuilder builder)
            => ColumnDefinition(
                operation.Schema,
                operation.Table,
                operation.Name,
                operation.ClrType,
                operation.ColumnType,
                operation.IsUnicode,
                operation.MaxLength,
                operation.IsFixedLength,
                operation.IsRowVersion,
                operation.IsNullable,
                operation.DefaultValue,
                operation.DefaultValueSql,
                operation.ComputedColumnSql,
                operation,
                model,
                builder);

        protected override void Generate(MigrationOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            if (operation is OracleCreateUserOperation createDatabaseOperation)
            {
                Generate(createDatabaseOperation, model, builder);
            }
            else if (operation is OracleDropUserOperation dropDatabaseOperation)
            {
                Generate(dropDatabaseOperation, model, builder);
            }
            else
            {
                base.Generate(operation, model, builder);
            }
        }

        protected override void Generate(
            AlterColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            var property = FindProperty(model, operation.Schema, operation.Table, operation.Name);

            if (operation.ComputedColumnSql != null)
            {
                var dropColumnOperation = new DropColumnOperation
                {
                    Schema = operation.Schema,
                    Table = operation.Table,
                    Name = operation.Name
                };

                var addColumnOperation = new AddColumnOperation
                {
                    Schema = operation.Schema,
                    Table = operation.Table,
                    Name = operation.Name,
                    ClrType = operation.ClrType,
                    ColumnType = operation.ColumnType,
                    IsUnicode = operation.IsUnicode,
                    MaxLength = operation.MaxLength,
                    IsRowVersion = operation.IsRowVersion,
                    IsNullable = operation.IsNullable,
                    DefaultValue = operation.DefaultValue,
                    DefaultValueSql = operation.DefaultValueSql,
                    ComputedColumnSql = operation.ComputedColumnSql,
                    IsFixedLength = operation.IsFixedLength
                };

                addColumnOperation.AddAnnotations(operation.GetAnnotations());

                Generate(dropColumnOperation, model, builder);
                Generate(addColumnOperation, model, builder);

                return;
            }

            var valueGenerationStrategy = operation[
                OracleAnnotationNames.ValueGenerationStrategy] as OracleValueGenerationStrategy?;
            var identity = valueGenerationStrategy == OracleValueGenerationStrategy.IdentityColumn;

            if (IsOldColumnSupported(model))
            {
                var oldValueGenerationStrategy = operation.OldColumn[
                    OracleAnnotationNames.ValueGenerationStrategy] as OracleValueGenerationStrategy?;
                var oldIdentity = oldValueGenerationStrategy == OracleValueGenerationStrategy.IdentityColumn;

                if (oldIdentity
                    && !identity)
                {
                    DropIdentity(operation, builder);
                }

                if (operation.OldColumn.DefaultValue != null
                    || operation.OldColumn.DefaultValueSql != null
                    && (operation.DefaultValue == null
                        || operation.DefaultValueSql == null))
                {
                    DropDefaultConstraint(operation.Schema, operation.Table, operation.Name, builder);
                }
            }
            else
            {
                if (!identity)
                {
                    DropIdentity(operation, builder);
                }

                if (operation.DefaultValue == null
                    && operation.DefaultValueSql == null)
                {
                    DropDefaultConstraint(operation.Schema, operation.Table, operation.Name, builder);
                }
            }

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" MODIFY ");

            ColumnDefinition(
                operation.Schema,
                operation.Table,
                operation.Name,
                operation.ClrType,
                operation.ColumnType,
                operation.IsUnicode,
                operation.MaxLength,
                operation.IsFixedLength,
                operation.IsRowVersion,
                operation.IsNullable,
                operation.DefaultValue,
                operation.DefaultValueSql,
                operation.ComputedColumnSql,
                identity,
                operation,
                model,
                builder);

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
        }

        private static void DropIdentity(
            AlterColumnOperation operation,
            MigrationCommandListBuilder builder)
        {
            var commandText = $@"
DECLARE
   v_Count INTEGER;
BEGIN
  SELECT COUNT(*) INTO v_Count
  FROM ALL_TAB_IDENTITY_COLS T
  WHERE T.TABLE_NAME = N'{operation.Table}'
  AND T.COLUMN_NAME = '{operation.Name}';
  IF v_Count > 0 THEN
    EXECUTE IMMEDIATE 'ALTER  TABLE ""{operation.Table}"" MODIFY ""{operation.Name}"" DROP IDENTITY';
  END IF;
END;";
            builder
                .AppendLine(commandText)
                .EndCommand();
        }

        protected override void Generate(
            RenameIndexOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            if (operation.NewName != null)
            {
                builder
                   .Append("ALTER INDEX ")
                   .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                   .Append(" RENAME TO ")
                   .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName));
            }

            builder.EndCommand();
        }

        protected override void SequenceOptions(
            string schema,
            string name,
            int increment,
            long? minimumValue,
            long? maximumValue,
            bool cycle,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            var intTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(int));
            var longTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(long));

            builder
                .Append(" INCREMENT BY ")
                .Append(intTypeMapping.GenerateSqlLiteral(increment));

            if (minimumValue != null)
            {
                builder
                    .Append(" MINVALUE ")
                    .Append(longTypeMapping.GenerateSqlLiteral(minimumValue));
            }
            else
            {
                builder.Append(" NOMINVALUE");
            }

            if (maximumValue != null)
            {
                builder
                    .Append(" MAXVALUE ")
                    .Append(longTypeMapping.GenerateSqlLiteral(maximumValue));
            }
            else
            {
                builder.Append(" NOMAXVALUE");
            }

            builder.Append(cycle ? " CYCLE" : " NOCYCLE");
        }

        protected override void Generate(
            RenameSequenceOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            if (operation.NewName != null)
            {
                builder
                    .Append("RENAME ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .Append(" TO ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName))
                    .EndCommand();
            }
        }

        protected override void Generate(
            RenameTableOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            if (operation.NewName != null)
            {
                builder
                   .Append("ALTER TABLE ")
                   .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                   .Append(" RENAME TO ")
                   .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName))
                   .EndCommand();
            }

            if (operation.NewSchema != null)
            {
                builder
                    .Append("RENAME ")
                    .Append(operation.NewSchema)
                    .Append(" TO ")
                    .Append(operation.Schema)
                    .EndCommand();
            }
        }

        protected override void Generate(
            DropPrimaryKeyOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            base.Generate(operation, model, builder, terminate: false);

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand();
        }

        protected virtual void Generate(
            OracleCreateUserOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            builder
                .Append(
                    $@"BEGIN
                             EXECUTE IMMEDIATE 'CREATE USER {operation.UserName} IDENTIFIED BY {operation.Password}';
                             EXECUTE IMMEDIATE 'GRANT DBA TO {operation.UserName}';
                           END;")
                .EndCommand(suppressTransaction: true);
        }

        protected virtual void Generate(
            OracleDropUserOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            builder
                .Append(
                    $@"BEGIN
                         FOR v_cur IN (SELECT sid, serial# FROM v$session WHERE username = '{operation.UserName.ToUpperInvariant()}') LOOP
                            EXECUTE IMMEDIATE ('ALTER SYSTEM KILL SESSION ''' || v_cur.sid || ',' || v_cur.serial# || ''' IMMEDIATE');
                         END LOOP;
                         EXECUTE IMMEDIATE 'DROP USER {operation.UserName} CASCADE';
                       END;")
                .EndCommand(suppressTransaction: true);
        }

        protected override void Generate(
            DropIndexOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        protected virtual void Generate(
            DropIndexOperation operation,
            IModel model,
            MigrationCommandListBuilder builder,
            bool terminate)
        {
            builder
                .Append("DROP INDEX ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand();
            }
        }

        protected override void Generate(
            RenameColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            var qualifiedName = new StringBuilder();
            if (operation.Schema != null)
            {
                qualifiedName
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Schema))
                    .Append(".");
            }

            qualifiedName.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table));

            builder
                .Append("ALTER TABLE ")
                .Append(qualifiedName)
                .Append(" RENAME COLUMN ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" TO ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName));

            builder.EndCommand();
        }

        protected override void Generate(
            InsertDataOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            var sqlBuilder = new StringBuilder();
            foreach (var modificationCommand in operation.GenerateModificationCommands(model))
            {
                SqlGenerator.AppendInsertOperation(
                    sqlBuilder,
                    modificationCommand,
                    0);
            }

            builder
                .AppendLine("BEGIN")
                .Append(sqlBuilder)
                .Append("END")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            builder.EndCommand();
        }

        protected override void Generate(CreateTableOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            base.Generate(operation, model, builder);

            var rowVersionColumns = operation.Columns.Where(c => c.IsRowVersion).ToArray();

            if (rowVersionColumns.Length > 0)
            {
                builder
                    .Append("CREATE OR REPLACE TRIGGER ")
                    .AppendLine(Dependencies.SqlGenerationHelper.DelimitIdentifier("rowversion_" + operation.Name, operation.Schema))
                    .Append("BEFORE INSERT OR UPDATE ON ")
                    .AppendLine(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
                    .AppendLine("FOR EACH ROW")
                    .AppendLine("BEGIN");

                foreach (var rowVersionColumn in rowVersionColumns)
                {
                    builder
                        .Append("  :NEW.")
                        .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(rowVersionColumn.Name))
                        .Append(" := UTL_RAW.CAST_FROM_BINARY_INTEGER(UTL_RAW.CAST_TO_BINARY_INTEGER(NVL(:OLD.")
                        .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(rowVersionColumn.Name))
                        .Append(", '00000000')) + 1)")
                        .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                }

                builder
                    .Append("END")
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }

            EndStatement(builder);
        }

        protected override void ColumnDefinition(
            string schema,
            string table,
            string name,
            Type clrType,
            string type,
            bool? unicode,
            int? maxLength,
            bool? fixedLength,
            bool rowVersion,
            bool nullable,
            object defaultValue,
            string defaultValueSql,
            string computedColumnSql,
            IAnnotatable annotatable,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            var valueGenerationStrategy = annotatable[
                OracleAnnotationNames.ValueGenerationStrategy] as OracleValueGenerationStrategy?;

            ColumnDefinition(
                schema,
                table,
                name,
                clrType,
                type,
                unicode,
                maxLength,
                fixedLength,
                rowVersion,
                nullable,
                defaultValue,
                defaultValueSql,
                computedColumnSql,
                valueGenerationStrategy == OracleValueGenerationStrategy.IdentityColumn,
                annotatable,
                model,
                builder);
        }

        protected virtual void ColumnDefinition(
            string schema,
            string table,
            string name,
            Type clrType,
            string type,
            bool? unicode,
            int? maxLength,
            bool? fixedLength,
            bool rowVersion,
            bool nullable,
            object defaultValue,
            string defaultValueSql,
            string computedColumnSql,
            bool identity,
            IAnnotatable annotatable,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            if (computedColumnSql != null)
            {
                builder
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
                    .Append(" AS (")
                    .Append(computedColumnSql)
                    .Append(")");

                return;
            }

            builder
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
                .Append(" ")
                .Append(type ?? GetColumnType(schema, table, name, clrType, unicode, maxLength, fixedLength, rowVersion, model));

            if (identity)
            {
                builder.Append(" GENERATED BY DEFAULT ON NULL AS IDENTITY");
            }
            else
            {
                DefaultValue(defaultValue, defaultValueSql, builder);
            }

            if (!nullable)
            {
                builder.Append(" NOT NULL");
            }
        }

        protected override void ForeignKeyConstraint(
            AddForeignKeyOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            if (operation.Name != null)
            {
                builder
                    .Append("CONSTRAINT ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .Append(" ");
            }

            builder
                .Append("FOREIGN KEY (")
                .Append(ColumnList(operation.Columns))
                .Append(") REFERENCES ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.PrincipalTable, operation.PrincipalSchema));

            if (operation.PrincipalColumns != null)
            {
                builder
                    .Append(" (")
                    .Append(ColumnList(operation.PrincipalColumns))
                    .Append(")");
            }

            if (operation.OnUpdate != ReferentialAction.NoAction)
            {
                builder.Append(" ON UPDATE ");
                ForeignKeyAction(operation.OnUpdate, builder);
            }

            if (operation.OnDelete != ReferentialAction.NoAction
                && operation.OnDelete != ReferentialAction.Restrict)
            {
                builder.Append(" ON DELETE ");
                ForeignKeyAction(operation.OnDelete, builder);
            }
        }

        protected virtual void DropDefaultConstraint(
            string schema,
            string tableName,
            string columnName,
            MigrationCommandListBuilder builder)
        {
            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName, schema))
                .Append(" MODIFY ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(columnName))
                .Append(" DEFAULT NULL")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand();
        }
    }
}
