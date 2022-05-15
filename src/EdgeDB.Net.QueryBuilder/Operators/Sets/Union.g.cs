using System.Linq.Expressions;

namespace EdgeDB.Operators
{
    internal class SetsUnion : IEdgeQLOperator
    {
        public ExpressionType? ExpressionType => null;
        public string EdgeQLOperator => "{0} union {1}";
    }
}