//
//      Copyright (C) 2012 DataStax Inc.
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
//
﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;

namespace Cassandra.Data.Linq
{
    public abstract class CqlQueryBase<TEntity>  : Query
    {
        private Expression _expression;
        private IQueryProvider _table;

        internal CqlQueryBase()
        {
        }

        public Expression Expression
        {
            get { return _expression; }
        }

        internal void InternalInitialize(Expression expression,IQueryProvider table)
        {
            this._expression = expression;
            this._table = table;
        }

        internal CqlQueryBase(Expression expression, IQueryProvider table)
        {
            this._expression = expression;
            this._table = table;
        }
        
        public Type ElementType
        {
            get { return typeof(TEntity); }
        }

        public ITable GetTable() { return _table as ITable; }

        protected abstract string GetCql(out object[] values);

        public QueryTrace QueryTrace { get; protected set; }

        protected struct CqlQueryTag
        {
            public Session Session;
            public Dictionary<string, Tuple<string, object,int>> Mappings;
            public Dictionary<string, string> Alter;
        }

        protected IAsyncResult InternalBeginExecute(string cqlQuery, object[] values, Dictionary<string, Tuple<string, object,int>> mappingNames, Dictionary<string, string> alter, AsyncCallback callback, object state)
        {
            var session = GetTable().GetSession();
            var stmt = new SimpleStatement(cqlQuery).BindObjects(values);
            this.CopyQueryPropertiesTo(stmt);
            return session.BeginExecute(stmt,
                                new CqlQueryTag() { Mappings = mappingNames, Alter = alter, Session = session }, callback, state);
        }

        protected RowSet InternalEndExecute(IAsyncResult ar)
        {
            var tag = (CqlQueryTag)Session.GetTag(ar);
            var ctx = tag.Session;
            var outp = ctx.EndExecute(ar);
            QueryTrace = outp.Info.QueryTrace;
            return outp;
        }

        public abstract IAsyncResult BeginExecute(AsyncCallback callback, object state);

        protected override IAsyncResult BeginSessionExecute(Session session, object tag, AsyncCallback callback, object state)
        {
            if (!ReferenceEquals(GetTable().GetSession(), session))
                throw new ArgumentOutOfRangeException("session");
            return BeginExecute(callback, state);
        }

        protected override RowSet EndSessionExecute(Session session, IAsyncResult ar)
        {
            if (!ReferenceEquals(GetTable().GetSession(), session))
                throw new ArgumentOutOfRangeException("session");
            return InternalEndExecute(ar);
        }

        public override RoutingKey RoutingKey
        {
            get { return null; }
        }
    }

    public class CqlQuerySingleElement<TEntity> : CqlQueryBase<TEntity>
    {
        internal CqlQuerySingleElement(Expression expression, IQueryProvider table)
            : base(expression, table) { }


        protected override string GetCql(out object[] values)
        {
            var visitor = new CqlExpressionVisitor();
            visitor.Evaluate(Expression);
            return visitor.GetSelect(out values);
        }

        public override string ToString()
        {
            var visitor = new CqlExpressionVisitor();
            visitor.Evaluate(Expression);
            object[] _;
            return visitor.GetSelect(out _, false);
        }

        public new CqlQuerySingleElement<TEntity> SetConsistencyLevel(ConsistencyLevel? consistencyLevel)
        {
            base.SetConsistencyLevel(consistencyLevel);
            return this;
        }

        public new CqlQuerySingleElement<TEntity> SetSerialConsistencyLevel(ConsistencyLevel consistencyLevel)
        {
            base.SetSerialConsistencyLevel(consistencyLevel);
            return this;
        }

        public override IAsyncResult BeginExecute(AsyncCallback callback, object state)
        {
            bool withValues = GetTable().GetSession().BinaryProtocolVersion > 1;
            var visitor = new CqlExpressionVisitor();
            visitor.Evaluate(Expression);
            object[] values;
            var cql = visitor.GetSelect(out values, withValues);
            return InternalBeginExecute(cql, values, visitor.Mappings, visitor.Alter, callback, state);
        }

        public TEntity EndExecute(IAsyncResult ar)
        {
            using (var outp = InternalEndExecute(ar))
            {
                var row = outp.GetRows().FirstOrDefault();
                if (row == null)
                    if (((MethodCallExpression)Expression).Method.Name == "First")
                        throw new InvalidOperationException("Sequence contains no elements.");
                    else if (((MethodCallExpression)Expression).Method.Name == "FirstOrDefault")
                        return default(TEntity);

                var cols = outp.Columns;
                var colToIdx = new Dictionary<string, int>();
                for (int idx = 0; idx < cols.Length; idx++)
                    colToIdx.Add(cols[idx].Name, idx);

                var tag = (CqlQueryTag)Session.GetTag(ar);
                return CqlQueryTools.GetRowFromCqlRow<TEntity>(row, colToIdx, tag.Mappings, tag.Alter);
            }
        }

        public TEntity Execute()
        {
            return EndExecute(BeginExecute(null, null));
        }
    }


    public class CqlScalar<TEntity> : CqlQueryBase<TEntity>
    {
        internal CqlScalar(Expression expression, IQueryProvider table) :base(expression,table){}

        public TEntity Execute()
        {
            return EndExecute(BeginExecute(null, null));
        }

        public new CqlScalar<TEntity> SetConsistencyLevel(ConsistencyLevel? consistencyLevel)
        {
            base.SetConsistencyLevel(consistencyLevel);
            return this;
        }

        protected override string GetCql(out object[] values)
        {
            var visitor = new CqlExpressionVisitor();
            visitor.Evaluate(Expression);
            return visitor.GetCount(out values);
        }

        public override string ToString()
        {
            var visitor = new CqlExpressionVisitor();
            visitor.Evaluate(Expression);
            object[] _;
            return visitor.GetCount(out _, false);
        }

        public override IAsyncResult BeginExecute(AsyncCallback callback, object state)
        {
            bool withValues = GetTable().GetSession().BinaryProtocolVersion > 1;

            var visitor = new CqlExpressionVisitor();
            visitor.Evaluate(Expression);

            object[] values;
            var cql = visitor.GetCount(out values, withValues);
            return InternalBeginExecute(cql, values, visitor.Mappings, visitor.Alter, callback, state);
        }

        public TEntity EndExecute(IAsyncResult ar)
        {
            using (var outp = InternalEndExecute(ar))
            {
                QueryTrace = outp.Info.QueryTrace;

                var cols = outp.Columns;
                if (cols.Length != 1)
                    throw new InvalidOperationException("Single column is expected.");

                var rows = outp.GetRows();
                bool first = false;
                TEntity ret = default(TEntity);
                foreach (var row in rows)
                {
                    if (first == false)
                    {
                        ret = (TEntity)row[0];
                        first = true;
                    }
                    else
                        throw new InvalidOperationException("Single row is expected.");
                }
                if (!first)
                    throw new InvalidOperationException("Single row is expected.");
                return ret;
            }

            throw new InvalidOperationException();
        }
    }

    public class CqlQuery<TEntity> : CqlQueryBase<TEntity>, IQueryable, IQueryable<TEntity>, IOrderedQueryable
    {
        internal CqlQuery()
        {
            InternalInitialize(Expression.Constant(this), (Table<TEntity>)this);
        }

        internal CqlQuery(Expression expression, IQueryProvider table) : base(expression,table)
        {
        }

        public new CqlQuery<TEntity> SetConsistencyLevel(ConsistencyLevel? consistencyLevel)
        {
            base.SetConsistencyLevel(consistencyLevel);
            return this;
        }

        public new CqlQuery<TEntity> SetSerialConsistencyLevel(ConsistencyLevel consistencyLevel)
        {
            base.SetSerialConsistencyLevel(consistencyLevel);
            return this;
        }
        
        public IEnumerator<TEntity> GetEnumerator()
        {
            throw new InvalidOperationException("Did you forget to Execute()?");
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IQueryProvider Provider
        {
            get { return GetTable() as IQueryProvider; }
        }

        protected override string GetCql(out object[] values)
        {
            var visitor = new CqlExpressionVisitor();
            visitor.Evaluate(Expression);
            return visitor.GetSelect(out values);    
        }

        public override string ToString()
        {
            var visitor = new CqlExpressionVisitor();
            visitor.Evaluate(Expression);
            object[] _;
            return visitor.GetSelect(out _, false);
        }

        public IEnumerable<TEntity> Execute()
        {
            return EndExecute(BeginExecute(null, null));
        }

        public override IAsyncResult BeginExecute(AsyncCallback callback, object state)
        {
            bool withValues = GetTable().GetSession().BinaryProtocolVersion > 1;

            var visitor = new CqlExpressionVisitor();
            visitor.Evaluate(Expression);
            object[] values;
            var cql = visitor.GetSelect(out values, withValues);

            return InternalBeginExecute(cql, values, visitor.Mappings, visitor.Alter, callback, state);
        }

        public IEnumerable<TEntity> EndExecute(IAsyncResult ar)
        {
            using (var outp = InternalEndExecute(ar))
            {
                QueryTrace = outp.Info.QueryTrace;

                var cols = outp.Columns;
                var colToIdx = new Dictionary<string, int>();
                for (int idx = 0; idx < cols.Length; idx++)
                    colToIdx.Add(cols[idx].Name, idx);
                var rows = outp.GetRows();
                var tag = (CqlQueryTag)Session.GetTag(ar);
                foreach (var row in rows)
                {
                    yield return CqlQueryTools.GetRowFromCqlRow<TEntity>(row, colToIdx, tag.Mappings, tag.Alter);
                }
            }
        }
    }

    public abstract class CqlCommand : SimpleStatement
    {
        protected abstract string GetCql(out object[] values);
        public void Execute()
        {
            EndExecute(BeginExecute(null, null));
        }

        public override string QueryString
        {
            get
            {
                if (base.QueryString == null)
                    InitializeStatement();
                return base.QueryString;
            }
        }

        public override object[] QueryValues
        {
            get
            {
                if (base.QueryString == null)
                    InitializeStatement();
                return base.QueryValues;
            }
        }

        protected int? _ttl = null;
        protected DateTimeOffset? _timestamp = null;
        private readonly Expression _expression;
        private readonly IQueryProvider _table;

        public void SetQueryTrace(QueryTrace trace)
        {
            QueryTrace = trace;
        }

        public new CqlCommand SetConsistencyLevel(ConsistencyLevel? consistencyLevel)
        {
            base.SetConsistencyLevel(consistencyLevel);
            return this;
        }

        public new CqlCommand SetSerialConsistencyLevel(ConsistencyLevel consistencyLevel)
        {
            base.SetSerialConsistencyLevel(consistencyLevel);
            return this;
        }

        public CqlCommand SetTTL(int seconds)
        {
            _ttl = seconds;
            return this;
        }

        public CqlCommand SetTimestamp(DateTimeOffset timestamp)
        {
            _timestamp = timestamp;
            return this;
        }

        internal CqlCommand(Expression expression, IQueryProvider table)
        {
            this._expression = expression;
            this._table = table;

        }

        protected void InitializeStatement()
        {
            object[] values;
            var query = GetCql(out values);
            SetQueryString(query);
            BindObjects(values);
        }

        public ITable GetTable()
        {
            return (_table as ITable);
        }

        public Expression Expression
        {
            get { return _expression; }
        }

        public QueryTrace QueryTrace { get; private set; }

        protected  override IAsyncResult BeginSessionExecute(Session session, object tag, AsyncCallback callback, object state)
        {
            if (!ReferenceEquals(GetTable().GetSession(), session))
                throw new ArgumentOutOfRangeException("session");
            return InternalBeginExecute(callback, state);
        }

        protected  override RowSet EndSessionExecute(Session session, IAsyncResult ar)
        {
            if (!ReferenceEquals(GetTable().GetSession(), session))
                throw new ArgumentOutOfRangeException("session");
            return InternalEndExecute(ar);
        }

        protected struct CqlQueryTag
        {
            public Session Session;
        }

        protected IAsyncResult InternalBeginExecute(AsyncCallback callback, object state)
        {
            InitializeStatement();
            var session = GetTable().GetSession();
            return base.BeginSessionExecute(session, new CqlQueryTag() { Session = session }, callback, state);
        }

        protected RowSet InternalEndExecute(IAsyncResult ar)
        {
            var tag = (CqlQueryTag)Session.GetTag(ar);
            var ctx = tag.Session;
            var outp = base.EndSessionExecute(ctx, ar);
            QueryTrace = outp.Info.QueryTrace;
            return outp;
        }

        public virtual IAsyncResult BeginExecute(AsyncCallback callback, object state)
        {
            return InternalBeginExecute(callback, state);
        }

        public virtual void EndExecute(IAsyncResult ar)
        {
            InternalEndExecute(ar);
        }
    }

    public class CqlDelete : CqlCommand
    {
        internal CqlDelete(Expression expression, IQueryProvider table)
            : base(expression, table)
        {
        }

        protected override string GetCql(out object[] values)
        {
            var withValues = GetTable().GetSession().BinaryProtocolVersion > 1;
            var visitor = new CqlExpressionVisitor();
            visitor.Evaluate(Expression);
            return visitor.GetDelete(out values, _timestamp, withValues);
        }

        public override string ToString()
        {
            var visitor = new CqlExpressionVisitor();
            visitor.Evaluate(Expression);
            object[] _;
            return visitor.GetDelete(out _, _timestamp, false);
        }
    }

    public class CqlInsert<TEntity> : CqlCommand
    {
        private readonly TEntity _entity;
        private bool _ifNotExists = false;

        internal CqlInsert(TEntity entity, IQueryProvider table)
            : base(null, table)
        {
            this._entity = entity;
        }

        public CqlInsert<TEntity> IfNotExists()
        {
            _ifNotExists = true;
            return this;
        }

        protected override string GetCql(out object[] values)
        {
            var withValues = GetTable().GetSession().BinaryProtocolVersion > 1;
            return CqlQueryTools.GetInsertCQLAndValues(_entity, (GetTable()).GetQuotedTableName(), out values, _ttl, _timestamp, _ifNotExists, withValues);
        }

        public override string ToString()
        {
            object[] _;
            return CqlQueryTools.GetInsertCQLAndValues(_entity, (GetTable()).GetQuotedTableName(), out _, _ttl, _timestamp, _ifNotExists, false);
        }
    }

    public class CqlUpdate : CqlCommand
    {
        internal CqlUpdate(Expression expression, IQueryProvider table)
            : base(expression, table)
        {
        }

        protected override string GetCql(out object[] values)
        {
            var withValues = GetTable().GetSession().BinaryProtocolVersion > 1;
            var visitor = new CqlExpressionVisitor();
            visitor.Evaluate(Expression);
            return visitor.GetUpdate(out values, _ttl, _timestamp,withValues);   
        }

        public override string ToString()
        {
            object[] _;
            var visitor = new CqlExpressionVisitor();
            visitor.Evaluate(Expression);
            return visitor.GetUpdate(out _, _ttl, _timestamp,false);
        }
    }
}