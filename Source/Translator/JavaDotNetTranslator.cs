namespace Janett.Translator
{
	using System;

	using Commons;

	using Framework;

	using ICSharpCode.NRefactory;

	[Named("janett")]
	public class JavaDotNetTranslator : Translator
	{
		protected override void ValidateParameters()
		{
			if (Mode != "DotNet" && Mode != "IKVM")
				throw new ApplicationException("Invalid value for 'Mode' parameter. Possible values: IKVM, DotNet");
		}

		protected override void BeforeParse()
		{
			AddJavaLangObjectBaseType objectBaseTypeAdder = new AddJavaLangObjectBaseType();
			objectBaseTypeAdder.CodeBase = codeBase;
			codeBase.Types.Visitors.Add(objectBaseTypeAdder);
			if (Mode == GetMode(typeof(ExternalInterfaceTransformer)))
			{
				ExternalInterfaceTransformer externalInterfaceTransformer = new ExternalInterfaceTransformer();
				externalInterfaceTransformer.CodeBase = codeBase;
				codeBase.Types.Visitors.Add(externalInterfaceTransformer);
			}
		}

		protected override void BeforeTransformation()
		{
			CallVisitor(typeof(AddJavaLangObjectBaseType), null);
		}

		protected override void AfterTransformation()
		{
			CallVisitor(typeof(SameProjectAndExternalTypeNameTransformer), null);
		}

		protected override void BeforeRefactoring()
		{
			if (Mode == "DotNet")
				CallVisitor(typeof(RemoveJavaLangObjectBaseType), null);
			CallVisitor(typeof(SuperUsageTransformer), null);
		}

		public override SupportedLanguage GetLanguage()
		{
			return SupportedLanguage.Java;
		}
	}
}