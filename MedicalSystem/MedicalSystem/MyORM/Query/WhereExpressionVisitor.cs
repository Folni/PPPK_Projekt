using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using MyORM.Attributes;

namespace MyORM.Query;

/// <summary>
/// Visits a LINQ expression tree and translates it into a PostgreSQL WHERE clause.
/// Supports: ==, !=, >, <, >=, <=, &&, ||, string.Contains, string.StartsWith, string.EndsWith
/// </summary>
public class WhereExpressionVisitor : ExpressionVisitor
{
    private readonly StringBuilder _sql = new();
    private readonly List<(string Name, object? Value)> _parameters = new();
    private int _paramIndex = 0;

    public string GetSql() => _sql.ToString();
    public List<(string Name, object? Value)> GetParameters() => _parameters;

    protected override Expression VisitBinary(BinaryExpression node)
    {
        _sql.Append('(');
        Visit(node.Left);

        _sql.Append(node.NodeType switch
        {
            ExpressionType.Equal => " = ",
            ExpressionType.NotEqual => " != ",
            ExpressionType.GreaterThan => " > ",
            ExpressionType.LessThan => " < ",
            ExpressionType.GreaterThanOrEqual => " >= ",
            ExpressionType.LessThanOrEqual => " <= ",
            ExpressionType.AndAlso => " AND ",
            ExpressionType.OrElse => " OR ",
            _ => throw new NotSupportedException($"Operator {node.NodeType} not supported")
        });

        Visit(node.Right);
        _sql.Append(')');
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression is ParameterExpression)
        {
            // It's a property access on the entity (e.g. p.FirstName)
            var prop = node.Member as PropertyInfo;
            var colAttr = prop?.GetCustomAttribute<ColumnAttribute>();
            var colName = colAttr?.Name ?? node.Member.Name.ToLower();
            _sql.Append(colName);
        }
        else
        {
            // It's a captured variable – evaluate it
            var value = GetValue(node);
            var paramName = $"@p{_paramIndex++}";
            _parameters.Add((paramName, value));
            _sql.Append(paramName);
        }
        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        var paramName = $"@p{_paramIndex++}";
        _parameters.Add((paramName, node.Value));
        _sql.Append(paramName);
        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.DeclaringType == typeof(string))
        {
            switch (node.Method.Name)
            {
                case "Contains":
                    Visit(node.Object!);
                    _sql.Append(" ILIKE ");
                    var containsVal = GetValue(node.Arguments[0]);
                    var cp = $"@p{_paramIndex++}";
                    _parameters.Add((cp, $"%{containsVal}%"));
                    _sql.Append(cp);
                    return node;

                case "StartsWith":
                    Visit(node.Object!);
                    _sql.Append(" ILIKE ");
                    var swVal = GetValue(node.Arguments[0]);
                    var sp = $"@p{_paramIndex++}";
                    _parameters.Add((sp, $"{swVal}%"));
                    _sql.Append(sp);
                    return node;

                case "EndsWith":
                    Visit(node.Object!);
                    _sql.Append(" ILIKE ");
                    var ewVal = GetValue(node.Arguments[0]);
                    var ep = $"@p{_paramIndex++}";
                    _parameters.Add((ep, $"%{ewVal}"));
                    _sql.Append(ep);
                    return node;
            }
        }

        throw new NotSupportedException($"Method '{node.Method.Name}' is not supported in Where expressions.");
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            _sql.Append("NOT ");
            Visit(node.Operand);
            return node;
        }
        return base.VisitUnary(node);
    }

    private static object? GetValue(Expression expression)
    {
        return Expression.Lambda(expression).Compile().DynamicInvoke();
    }
}
