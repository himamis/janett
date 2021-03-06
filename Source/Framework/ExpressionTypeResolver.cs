namespace Janett.Framework
{
	using System;
	using System.Collections;
	using System.Collections.Generic;

	using ICSharpCode.NRefactory.Ast;

	public class ExpressionTypeResolver
	{
		public TypeResolver TypeResolver;
		public AstUtil AstUtil;
		public CodeBase CodeBase;
		private IDictionary primitiveTypeMappings;

		public ExpressionTypeResolver()
		{
			primitiveTypeMappings = new Hashtable();
			foreach (KeyValuePair<string, string> entry in TypeReference.PrimitiveTypesCSharp)
				primitiveTypeMappings.Add(entry.Value, entry.Key);
		}

		public TypeReference GetType(Expression ex)
		{
			return GetType(ex, ex.Parent);
		}

		public TypeReference GetType(Expression ex, INode parent)
		{
			if (ex is IdentifierExpression)
			{
				return GetIdentifierType(ex, parent);
			}
			else if (ex is FieldReferenceExpression)
			{
				FieldReferenceExpression fieldReference = (FieldReferenceExpression) ex;
				Expression targetObject = fieldReference.TargetObject;
				TypeReference targetType = GetType(targetObject);
				if (targetType != null)
				{
					string fullName = TypeResolver.GetFullName(targetType);
					if (targetType.RankSpecifier != null && targetType.RankSpecifier.Length > 0 && !(ex.Parent is IndexerExpression))
						fullName = "JavaArray";
					if (CodeBase.Types.Contains(fullName))
					{
						TypeDeclaration typeDeclaration = (TypeDeclaration) CodeBase.Types[fullName];
						if (typeDeclaration.Type == ClassType.Enum)
							return AstUtil.GetTypeReference(typeDeclaration.Name, ex.Parent);
						else
							return GetTypeInMembers(typeDeclaration, fieldReference.FieldName);
					}
				}
				return null;
			}
			else if (ex is PrimitiveExpression)
			{
				return GetConstantType((PrimitiveExpression) ex);
			}
			else if (ex is InvocationExpression)
			{
				return GetType(((InvocationExpression) ex).TargetObject);
			}
			else if (ex is IndexerExpression)
			{
				return GetType(((IndexerExpression) ex).TargetObject);
			}
			else if (ex is BinaryOperatorExpression)
			{
				return GetType(((BinaryOperatorExpression) ex).Left);
			}
			else if (ex is ObjectCreateExpression)
			{
				return ((ObjectCreateExpression) ex).CreateType;
			}
			else if (ex is ThisReferenceExpression)
			{
				TypeDeclaration ty = (TypeDeclaration) AstUtil.GetParentOfType(ex, typeof(TypeDeclaration));
				return AstUtil.GetTypeReference(ty.Name, ex.Parent);
			}
			else if (ex is CastExpression)
			{
				CastExpression cast = (CastExpression) ex;
				return cast.CastTo;
			}
			else if (ex is ArrayCreateExpression)
			{
				return ((ArrayCreateExpression) ex).CreateType;
			}
			else if (ex is BaseReferenceExpression)
			{
				TypeDeclaration typeDeclaration = (TypeDeclaration) AstUtil.GetParentOfType(ex, typeof(TypeDeclaration));
				if (typeDeclaration.BaseTypes.Count > 0)
					return (TypeReference) typeDeclaration.BaseTypes[0];
				else
					return AstUtil.GetTypeReference("System.Object", parent);
			}
			else if (ex is ParenthesizedExpression)
			{
				return GetType(((ParenthesizedExpression) ex).Expression);
			}
			else if (ex is TypeReferenceExpression)
			{
				return ((TypeReferenceExpression) ex).TypeReference;
			}
			else if (ex is AssignmentExpression)
			{
				return GetType(((AssignmentExpression) ex).Left);
			}
			else if (ex is UnaryOperatorExpression)
			{
				return GetType(((UnaryOperatorExpression) ex).Expression);
			}
			else if (ex is TypeOfExpression)
			{
				TypeReference typeReference = new TypeReference("java.lang.Class");
				typeReference.Parent = parent;
				return typeReference;
			}
			else if (ex is TypeOfIsExpression)
			{
				return GetType(((TypeOfIsExpression) ex).Expression);
			}
			else if (ex is ConditionalExpression)
			{
				return GetType(((ConditionalExpression) ex).TrueExpression);
			}
			else if (ex is ParameterDeclarationExpression)
			{
				return ((ParameterDeclarationExpression) ex).TypeReference;
			}
			else if (ex == Expression.Null)
			{
				return null;
			}
			else
				throw new NotSupportedException(ex.GetType().Name);
		}

		protected TypeReference GetIdentifierType(Expression ex, INode parent)
		{
			string identifier = ((IdentifierExpression) ex).Identifier;
			TypeReference typeReference = null;
			INode parentScope = parent;
			while (parentScope != null)
			{
				if (parentScope is CastExpression)
				{
					CastExpression castExpression = (CastExpression) parentScope;
					if (castExpression.Expression is IdentifierExpression &&
					    ((IdentifierExpression) castExpression.Expression).Identifier == identifier)
						return castExpression.CastTo;
				}
				else if (parentScope is BlockStatement)
				{
					typeReference = GetTypeInLocalVariables((BlockStatement) parentScope, identifier);
					if (typeReference != null)
						return typeReference;
				}
				else if (parentScope is CatchClause)
				{
					string catchVar = ((CatchClause) parentScope).VariableName;
					if (catchVar == identifier)
						return ((CatchClause) parentScope).TypeReference;
				}
				else if (parentScope is ForStatement)
				{
					ForStatement forStatement = (ForStatement) parentScope;
					if (forStatement.Initializers.Count > 0)
					{
						Statement variable = (Statement) forStatement.Initializers[0];
						if (variable is LocalVariableDeclaration)
						{
							LocalVariableDeclaration localVariable = (LocalVariableDeclaration) variable;
							if (((VariableDeclaration) localVariable.Variables[0]).Name == identifier)
								return localVariable.TypeReference;
						}
					}
				}
				else if (parentScope is ForeachStatement ||
				         parentScope is MethodDeclaration ||
				         parentScope is ConstructorDeclaration ||
				         parentScope is TypeDeclaration)
					break;
				parentScope = parentScope.Parent;
			}
			if (parentScope is ConstructorDeclaration || parentScope is MethodDeclaration)
			{
				BlockStatement body = GetBody(parentScope);
				List<ParameterDeclarationExpression> parameters = ((ParametrizedNode) parentScope).Parameters;
				TypeDeclaration typeDeclaration = AstUtil.GetParentOfType(parentScope, typeof(TypeDeclaration)) as TypeDeclaration;
				typeReference = GetTypeInLocalVariables(body, identifier);

				if (typeReference == null)
					typeReference = GetTypeInArguments(parameters, identifier);
				if (typeReference == null)
					typeReference = GetTypeInMembers(typeDeclaration, identifier);
				if (typeReference == null)
					typeReference = GetStaticClassType(identifier, ex.Parent);
			}
			else if (parentScope is TypeDeclaration)
			{
				typeReference = GetTypeInMembers((TypeDeclaration) parentScope, identifier);
				if (typeReference == null)
					typeReference = GetStaticClassType(identifier, ex.Parent);
			}
			else if (parentScope is ForeachStatement)
			{
				string foreachVarName = ((ForeachStatement) parentScope).VariableName;
				TypeReference foreachVariableType = ((ForeachStatement) parentScope).TypeReference;
				if (identifier == foreachVarName)
					return foreachVariableType;
				else
					typeReference = GetType(ex, parentScope.Parent);
			}
			if (typeReference != null && typeReference.Parent == null)
				typeReference.Parent = parent;
			return typeReference;
		}

		protected TypeReference GetTypeInLocalVariables(BlockStatement block, string identifier)
		{
			foreach (LocalVariableDeclaration variableDeclaration in AstUtil.GetChildrenWithType(block, typeof(LocalVariableDeclaration)))
			{
				foreach (VariableDeclaration localVariable in variableDeclaration.Variables)
				{
					if (localVariable.Name == identifier)
						return variableDeclaration.TypeReference;
				}
			}
			return null;
		}

		protected TypeReference GetTypeInArguments(List<ParameterDeclarationExpression> parameters, string identifier)
		{
			foreach (ParameterDeclarationExpression parameter in parameters)
			{
				if (parameter.ParameterName == identifier)
				{
					return parameter.TypeReference;
				}
			}
			return null;
		}

		protected TypeReference GetTypeInMembers(TypeDeclaration typeDeclaration, string identifier)
		{
			IDictionary renamedKeywords = new Hashtable();
			renamedKeywords.Add("_out", "out");
			renamedKeywords.Add("_in", "in");

			foreach (FieldDeclaration fieldDeclaration in AstUtil.GetChildrenWithType(typeDeclaration, typeof(FieldDeclaration)))
			{
				foreach (VariableDeclaration field in fieldDeclaration.Fields)
				{
					bool isRenamedKeyword = renamedKeywords.Contains(identifier) && (field.Name == (string) renamedKeywords[identifier]);

					if (field.Name == identifier || isRenamedKeyword)
						return fieldDeclaration.TypeReference;
				}
			}
			foreach (PropertyDeclaration propertyDeclaration in AstUtil.GetChildrenWithType(typeDeclaration, typeof(PropertyDeclaration)))
			{
				if (propertyDeclaration.Name == identifier || "get" + propertyDeclaration.Name == identifier)
					return propertyDeclaration.TypeReference;
			}
			foreach (MethodDeclaration methodDeclaration in AstUtil.GetChildrenWithType(typeDeclaration, typeof(MethodDeclaration)))
			{
				if (methodDeclaration.Name == identifier)
					return methodDeclaration.TypeReference;
			}
			foreach (TypeDeclaration innerType in AstUtil.GetChildrenWithType(typeDeclaration, typeof(TypeDeclaration)))
			{
				if (innerType.Name == identifier)
					return AstUtil.GetTypeReference(typeDeclaration.Name + "." + innerType.Name, typeDeclaration.Parent);
			}
			if (typeDeclaration.BaseTypes.Count > 0)
			{
				string baseType = TypeResolver.GetFullName(((TypeReference) typeDeclaration.BaseTypes[0]));
				if (CodeBase.Types.Contains(baseType))
				{
					TypeDeclaration parentType = (TypeDeclaration) CodeBase.Types[baseType];
					return GetTypeInMembers(parentType, identifier);
				}
			}
			return null;
		}

		protected TypeReference GetStaticClassType(string identifier, INode parent)
		{
			if (Char.IsUpper(identifier[0]))
			{
				string fullName = TypeResolver.GetStaticFullName(identifier, parent);
				if (CodeBase.Types.Contains(fullName))
					return AstUtil.GetTypeReference(fullName, parent);
				else if (CodeBase.Mappings.Contains(fullName))
					return AstUtil.GetTypeReference(fullName, parent);
			}
			return null;
		}

		protected TypeReference GetConstantType(PrimitiveExpression expression)
		{
			if (expression.Value == null)
			{
				TypeReference nullType = TypeReference.Null;
				nullType.Parent = expression.Parent;
				return nullType;
			}
			else
			{
				string typeName = expression.Value.GetType().FullName;
				if (primitiveTypeMappings.Contains(typeName))
				{
					string csharpType = primitiveTypeMappings[typeName].ToString();
					string javaType;
					if (csharpType == "bool")
						javaType = "java.lang.Boolean";
					else if (csharpType == "string")
						javaType = "java.lang.String";
					else
					{
						if (csharpType.ToLower().StartsWith("u"))
							csharpType = csharpType.Remove(0, 1);
						javaType = (string) TypeReference.PrimitiveTypesJava[csharpType];
					}
					TypeReference typeReference = new TypeReference(javaType, javaType);
					typeReference.Parent = expression.Parent;
					return typeReference;
				}
				else
					throw new ApplicationException();
			}
		}

		private BlockStatement GetBody(INode methodOrConsturctor)
		{
			return (BlockStatement) methodOrConsturctor.GetType().GetProperty("Body").GetValue(methodOrConsturctor, null);
		}
	}
}