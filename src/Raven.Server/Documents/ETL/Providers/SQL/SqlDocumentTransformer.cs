using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;
using Raven.Client.Documents.Operations.ETL;
using Raven.Client.Documents.Operations.ETL.SQL;
using Raven.Server.Documents.Patch;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Documents.ETL.Providers.SQL
{
    internal class SqlDocumentTransformer : EtlTransformer<ToSqlItem, SqlTableWithRecords>
    {
        private static readonly JsValue DefaultVarCharSize = 50;
        
        private readonly Transformation _transformation;
        private readonly SqlEtlConfiguration _config;
        private readonly Dictionary<string, SqlTableWithRecords> _tables;
        private Dictionary<string, Queue<Attachment>> _loadedAttachments;

        public SqlDocumentTransformer(Transformation transformation, DocumentDatabase database, DocumentsOperationContext context, SqlEtlConfiguration config)
            : base(database, context, new PatchRequest(transformation.Script, PatchRequestType.SqlEtl), null)
        {
            _transformation = transformation;
            _config = config;
            _tables = new Dictionary<string, SqlTableWithRecords>(_config.SqlTables.Count);

            var tables = new string[config.SqlTables.Count];

            for (var i = 0; i < config.SqlTables.Count; i++)
            {
                tables[i] = config.SqlTables[i].TableName;
            }

            LoadToDestinations = tables;

            if (_transformation.IsLoadingAttachments)
               _loadedAttachments = new Dictionary<string, Queue<Attachment>>(StringComparer.OrdinalIgnoreCase);
        }

        public override void Initalize(bool debugMode)
        {
            base.Initalize(debugMode);
            
            DocumentScript.ScriptEngine.SetValue("varchar",
                new ClrFunctionInstance(DocumentScript.ScriptEngine, (value, values) => ToVarcharTranslator(VarcharFunctionCall.AnsiStringType, values)));

            DocumentScript.ScriptEngine.SetValue("nvarchar",
                new ClrFunctionInstance(DocumentScript.ScriptEngine, (value, values) => ToVarcharTranslator(VarcharFunctionCall.StringType, values)));
        }

        protected override string[] LoadToDestinations { get; }

        protected override void LoadToFunction(string tableName, ScriptRunnerResult cols)
        {
            if (tableName == null)
                ThrowLoadParameterIsMandatory(nameof(tableName));

            var result = cols.TranslateToObject(Context);
            var columns = new List<SqlColumn>(result.Count);
            var prop = new BlittableJsonReaderObject.PropertyDetails();

            for (var i = 0; i < result.Count; i++)
            {
                result.GetPropertyByIndex(i, ref prop);

                var sqlColumn = new SqlColumn
                {
                    Id = prop.Name,
                    Value = prop.Value,
                    Type = prop.Token
                };

                if (_transformation.IsLoadingAttachments && 
                    prop.Token == BlittableJsonToken.String && IsLoadAttachment(prop.Value as LazyStringValue, out var attachmentName))
                {
                    var attachment = _loadedAttachments[attachmentName].Dequeue();

                    sqlColumn.Type = 0;
                    sqlColumn.Value = attachment.Stream;
                }

                columns.Add(sqlColumn);
            }

            GetOrAdd(tableName).Inserts.Add(new ToSqlItem(Current)
            {
                Columns = columns
            });
        }

        private static unsafe bool IsLoadAttachment(LazyStringValue value, out string attachmentName)
        {
            if (value.Length <= Transformation.AttachmentMarker.Length)
            {
                attachmentName = null;
                return false;
            }

            var buffer = value.Buffer;

            if (*(long*)buffer != 7883660417928814884 || // $attachm
                *(int*)(buffer + 8) != 796159589) // ent/
            {
                attachmentName = null;
                return false;
            }

            attachmentName = value.Substring(Transformation.AttachmentMarker.Length);

            return true;
        }

        protected override void AddLoadedAttachment(JsValue reference, string name, Attachment attachment)
        {
            if (_loadedAttachments.TryGetValue(name, out var loadedAttachments) == false)
            {
                loadedAttachments = new Queue<Attachment>();
                _loadedAttachments.Add(name, loadedAttachments);
            }

            loadedAttachments.Enqueue(attachment);
        }

        protected override void AddLoadedCounter(JsValue reference, string name, long value)
        {
            throw new NotSupportedException("Counters aren't supported by SQL ETL");
        }

        private SqlTableWithRecords GetOrAdd(string tableName)
        {
            if (_tables.TryGetValue(tableName, out SqlTableWithRecords table) == false)
            {
                _tables[tableName] =
                    table = new SqlTableWithRecords(_config.SqlTables.Find(x => x.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase)));
            }

            return table;
        }

        public override List<SqlTableWithRecords> GetTransformedResults()
        {
            return _tables.Values.ToList();
        }

        public override void Transform(ToSqlItem item)
        {
            if (item.IsDelete == false)
            {
                Current = item;

                DocumentScript.Run(Context, Context, "execute", new object[] { Current.Document }).Dispose();
            }

            // ReSharper disable once ForCanBeConvertedToForeach
            for (int i = 0; i < _config.SqlTables.Count; i++)
            {
                // delete all the rows that might already exist there

                var sqlTable = _config.SqlTables[i];

                if (sqlTable.InsertOnlyMode)
                    continue;

                GetOrAdd(sqlTable.TableName).Deletes.Add(item);
            }
        }

        private JsValue ToVarcharTranslator(JsValue type, JsValue[] args)
        {
            if (args[0].IsString() == false)
                throw new InvalidOperationException("varchar() / nvarchar(): first argument must be a string");

            var sizeSpecified = args.Length > 1;

            if (sizeSpecified && args[1].IsNumber() == false)
                throw new InvalidOperationException("varchar() / nvarchar(): second argument must be a number");

            var item = DocumentScript.ScriptEngine.Object.Construct(Arguments.Empty);

            item.Put(nameof(VarcharFunctionCall.Type), type, true);
            item.Put(nameof(VarcharFunctionCall.Value), args[0], true);
            item.Put(nameof(VarcharFunctionCall.Size), sizeSpecified ? args[1] : DefaultVarCharSize, true);

            return item;
        }

        public class VarcharFunctionCall
        {
            public static JsValue AnsiStringType = DbType.AnsiString.ToString();
            public static JsValue StringType = DbType.String.ToString();

            public DbType Type { get; set; }
            public object Value { get; set; }
            public int Size { get; set; }

            private VarcharFunctionCall()
            {

            }
        }
    }
}
