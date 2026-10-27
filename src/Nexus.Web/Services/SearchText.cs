using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Nexus.Web.Services;

// Text search that ignores case and accents: "pagamento" finds "Pagamento", "especificacao" finds
// "Especificação" (issue #60). string.Contains became a case- and accent-sensitive LIKE in
// PostgreSQL. There it is unaccent(column) ILIKE '%term%' (unaccent extension, migration
// AddUnaccentExtension); on the in-memory provider used by tests, the same folding runs in .NET.
public static class SearchText
{
    /// <summary>
    /// Predicate true when any of <paramref name="columns"/> contains <paramref name="text"/>,
    /// ignoring case and accents, or when <paramref name="orElse"/> holds.
    /// </summary>
    public static Expression<Func<T, bool>> Matches<T>(DbContext db, string text, Expression<Func<T, bool>>? orElse,
        params Expression<Func<T, string?>>[] columns)
    {
        var relational = db.Database.IsRelational();
        var folded = Fold(text.Trim());
        var pattern = "%" + EscapeLike(folded) + "%";
        var row = Expression.Parameter(typeof(T), "row");

        Expression? body = null;
        foreach (var column in columns)
        {
            Expression<Func<string?, bool>> test = relational
                ? value => EF.Functions.ILike(EF.Functions.Unaccent(value!), pattern, @"\")
                : value => Fold(value).Contains(folded);
            var match = Inline(test, Inline(column, row));
            body = body is null ? match : Expression.OrElse(body, match);
        }

        if (orElse is not null)
        {
            var extra = Inline(orElse, row);
            body = body is null ? extra : Expression.OrElse(body, extra);
        }

        return Expression.Lambda<Func<T, bool>>(body ?? Expression.Constant(false), row);
    }

    /// <summary>Lowercase without diacritics: "Especificação" → "especificacao".</summary>
    public static string Fold(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    // "100%" must search for a literal percent sign, not match everything.
    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    // Substitutes the lambda's single parameter with an expression, so the pieces compose into one
    // tree EF can translate (Expression.Invoke is not translatable).
    private static Expression Inline<TIn, TOut>(Expression<Func<TIn, TOut>> lambda, Expression argument) =>
        new ParameterReplacer(lambda.Parameters[0], argument).Visit(lambda.Body);

    private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == parameter ? replacement : base.VisitParameter(node);
    }
}
