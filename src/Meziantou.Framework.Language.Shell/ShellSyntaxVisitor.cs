namespace Meziantou.Framework.Language.Shell;

/// <summary>Base visitor for walking shell syntax trees without returning a value.</summary>
public abstract class ShellSyntaxVisitor
{
    public virtual void Visit(ShellSyntaxNode? node)
    {
        if (node is null)
            return;

        node.Accept(this);
    }

    protected virtual void DefaultVisit(ShellSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        foreach (var child in node.ChildNodes)
        {
            Visit(child);
        }
    }

    public virtual void VisitScript(ShellScriptSyntax node) => DefaultVisit(node);
    public virtual void VisitStatementList(ShellStatementListSyntax node) => DefaultVisit(node);
    public virtual void VisitCommand(ShellCommandSyntax node) => DefaultVisit(node);
    public virtual void VisitPipeline(ShellPipelineSyntax node) => DefaultVisit(node);
    public virtual void VisitCommandList(ShellCommandListSyntax node) => DefaultVisit(node);
    public virtual void VisitRedirection(ShellRedirectionSyntax node) => DefaultVisit(node);
    public virtual void VisitAssignment(ShellAssignmentSyntax node) => DefaultVisit(node);
    public virtual void VisitWord(ShellWordSyntax node) => DefaultVisit(node);
    public virtual void VisitLiteralWordPart(ShellLiteralWordPartSyntax node) => DefaultVisit(node);
    public virtual void VisitQuotedString(ShellQuotedStringSyntax node) => DefaultVisit(node);
    public virtual void VisitVariableReference(ShellVariableReferenceSyntax node) => DefaultVisit(node);
    public virtual void VisitCommandSubstitution(ShellCommandSubstitutionSyntax node) => DefaultVisit(node);
    public virtual void VisitEscapeSequence(ShellEscapeSequenceSyntax node) => DefaultVisit(node);
    public virtual void VisitArithmeticExpansion(PosixArithmeticExpansionSyntax node) => DefaultVisit(node);
    public virtual void VisitGlob(ShellGlobSyntax node) => DefaultVisit(node);
    public virtual void VisitRawExpression(ShellRawExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitIfStatement(PosixIfStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitElifClause(PosixElifClauseSyntax node) => DefaultVisit(node);
    public virtual void VisitElseClause(PosixElseClauseSyntax node) => DefaultVisit(node);
    public virtual void VisitWhileStatement(PosixWhileStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitForStatement(PosixForStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitCaseStatement(PosixCaseStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitCaseClause(PosixCaseClauseSyntax node) => DefaultVisit(node);
    public virtual void VisitFunctionDefinition(PosixFunctionDefinitionSyntax node) => DefaultVisit(node);
    public virtual void VisitCompoundStatement(PosixCompoundStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitDelimitedExpressionStatement(PosixDelimitedExpressionStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitHereDocument(PosixHereDocumentSyntax node) => DefaultVisit(node);
    public virtual void VisitProcessSubstitution(PosixProcessSubstitutionSyntax node) => DefaultVisit(node);
    public virtual void VisitArrayAssignment(PosixArrayAssignmentSyntax node) => DefaultVisit(node);
    public virtual void VisitPrefixedStatement(PosixPrefixedStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitArrayLiteral(PowerShellArrayLiteralSyntax node) => DefaultVisit(node);
    public virtual void VisitAssignmentExpression(PowerShellAssignmentExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitAttribute(PowerShellAttributeSyntax node) => DefaultVisit(node);
    public virtual void VisitBinaryExpression(PowerShellBinaryExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitCatchClause(PowerShellCatchClauseSyntax node) => DefaultVisit(node);
    public virtual void VisitDataStatement(PowerShellDataStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitDoStatement(PowerShellDoStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitElseIfClause(PowerShellElseIfClauseSyntax node) => DefaultVisit(node);
    public virtual void VisitExpandableString(PowerShellExpandableStringSyntax node) => DefaultVisit(node);
    public virtual void VisitExpressionStatement(PowerShellExpressionStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitFinallyClause(PowerShellFinallyClauseSyntax node) => DefaultVisit(node);
    public virtual void VisitFlowStatement(PowerShellFlowStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitForEachStatement(PowerShellForEachStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitHashEntry(PowerShellHashEntrySyntax node) => DefaultVisit(node);
    public virtual void VisitHashLiteral(PowerShellHashLiteralSyntax node) => DefaultVisit(node);
    public virtual void VisitIndexExpression(PowerShellIndexExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitInvocation(PowerShellInvocationExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitLabeledStatement(PowerShellLabeledStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitMemberAccess(PowerShellMemberAccessExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitNamedBlock(PowerShellNamedBlockSyntax node) => DefaultVisit(node);
    public virtual void VisitParamBlock(PowerShellParamBlockSyntax node) => DefaultVisit(node);
    public virtual void VisitParameter(PowerShellParameterSyntax node) => DefaultVisit(node);
    public virtual void VisitParenthesizedExpression(PowerShellParenthesizedExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitPowerShellElseClause(PowerShellElseClauseSyntax node) => DefaultVisit(node);
    public virtual void VisitPowerShellForStatement(PowerShellForStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitPowerShellFunctionDefinition(PowerShellFunctionDefinitionSyntax node) => DefaultVisit(node);
    public virtual void VisitPowerShellIfStatement(PowerShellIfStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitPowerShellLiteral(PowerShellLiteralExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitPowerShellVariable(PowerShellVariableExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitPowerShellWhileStatement(PowerShellWhileStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitScriptBlock(PowerShellScriptBlockSyntax node) => DefaultVisit(node);
    public virtual void VisitSubExpression(PowerShellSubExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitSwitchClause(PowerShellSwitchClauseSyntax node) => DefaultVisit(node);
    public virtual void VisitSwitchStatement(PowerShellSwitchStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitTernaryExpression(PowerShellTernaryExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitTrapStatement(PowerShellTrapStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitTryStatement(PowerShellTryStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitTypeDefinition(PowerShellTypeDefinitionSyntax node) => DefaultVisit(node);
    public virtual void VisitTypeLiteral(PowerShellTypeLiteralSyntax node) => DefaultVisit(node);
    public virtual void VisitUnaryExpression(PowerShellUnaryExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitUsingStatement(PowerShellUsingStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitEmbeddedExpression(ShellEmbeddedExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitCastExpression(PowerShellCastExpressionSyntax node) => DefaultVisit(node);
    public virtual void VisitCmdBlock(CmdParenthesizedBlockSyntax node) => DefaultVisit(node);
    public virtual void VisitCmdCall(CmdCallStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitCmdElseClause(CmdElseClauseSyntax node) => DefaultVisit(node);
    public virtual void VisitCmdFor(CmdForStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitCmdGoto(CmdGotoStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitCmdIf(CmdIfStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitCmdLabel(CmdLabelStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitCmdSet(CmdSetStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitCmdVariableReference(CmdVariableReferenceSyntax node) => DefaultVisit(node);
    public virtual void VisitEmptyStatement(ShellEmptyStatementSyntax node) => DefaultVisit(node);
    public virtual void VisitSkippedText(ShellSkippedTextSyntax node) => DefaultVisit(node);
}

/// <summary>Base visitor for walking shell syntax trees and returning a value.</summary>
/// <typeparam name="TResult">Type returned by visit methods.</typeparam>
public abstract class ShellSyntaxVisitor<TResult>
{
    public virtual TResult Visit(ShellSyntaxNode? node)
    {
        if (node is null)
            return default!;

        return node.Accept(this);
    }

    protected virtual TResult DefaultVisit(ShellSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        foreach (var child in node.ChildNodes)
        {
            _ = Visit(child);
        }

        return default!;
    }

    public virtual TResult VisitScript(ShellScriptSyntax node) => DefaultVisit(node);
    public virtual TResult VisitStatementList(ShellStatementListSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCommand(ShellCommandSyntax node) => DefaultVisit(node);
    public virtual TResult VisitPipeline(ShellPipelineSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCommandList(ShellCommandListSyntax node) => DefaultVisit(node);
    public virtual TResult VisitRedirection(ShellRedirectionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitAssignment(ShellAssignmentSyntax node) => DefaultVisit(node);
    public virtual TResult VisitWord(ShellWordSyntax node) => DefaultVisit(node);
    public virtual TResult VisitLiteralWordPart(ShellLiteralWordPartSyntax node) => DefaultVisit(node);
    public virtual TResult VisitQuotedString(ShellQuotedStringSyntax node) => DefaultVisit(node);
    public virtual TResult VisitVariableReference(ShellVariableReferenceSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCommandSubstitution(ShellCommandSubstitutionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitEscapeSequence(ShellEscapeSequenceSyntax node) => DefaultVisit(node);
    public virtual TResult VisitArithmeticExpansion(PosixArithmeticExpansionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitGlob(ShellGlobSyntax node) => DefaultVisit(node);
    public virtual TResult VisitRawExpression(ShellRawExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitIfStatement(PosixIfStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitElifClause(PosixElifClauseSyntax node) => DefaultVisit(node);
    public virtual TResult VisitElseClause(PosixElseClauseSyntax node) => DefaultVisit(node);
    public virtual TResult VisitWhileStatement(PosixWhileStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitForStatement(PosixForStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCaseStatement(PosixCaseStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCaseClause(PosixCaseClauseSyntax node) => DefaultVisit(node);
    public virtual TResult VisitFunctionDefinition(PosixFunctionDefinitionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCompoundStatement(PosixCompoundStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitDelimitedExpressionStatement(PosixDelimitedExpressionStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitHereDocument(PosixHereDocumentSyntax node) => DefaultVisit(node);
    public virtual TResult VisitProcessSubstitution(PosixProcessSubstitutionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitArrayAssignment(PosixArrayAssignmentSyntax node) => DefaultVisit(node);
    public virtual TResult VisitPrefixedStatement(PosixPrefixedStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitArrayLiteral(PowerShellArrayLiteralSyntax node) => DefaultVisit(node);
    public virtual TResult VisitAssignmentExpression(PowerShellAssignmentExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitAttribute(PowerShellAttributeSyntax node) => DefaultVisit(node);
    public virtual TResult VisitBinaryExpression(PowerShellBinaryExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCatchClause(PowerShellCatchClauseSyntax node) => DefaultVisit(node);
    public virtual TResult VisitDataStatement(PowerShellDataStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitDoStatement(PowerShellDoStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitElseIfClause(PowerShellElseIfClauseSyntax node) => DefaultVisit(node);
    public virtual TResult VisitExpandableString(PowerShellExpandableStringSyntax node) => DefaultVisit(node);
    public virtual TResult VisitExpressionStatement(PowerShellExpressionStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitFinallyClause(PowerShellFinallyClauseSyntax node) => DefaultVisit(node);
    public virtual TResult VisitFlowStatement(PowerShellFlowStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitForEachStatement(PowerShellForEachStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitHashEntry(PowerShellHashEntrySyntax node) => DefaultVisit(node);
    public virtual TResult VisitHashLiteral(PowerShellHashLiteralSyntax node) => DefaultVisit(node);
    public virtual TResult VisitIndexExpression(PowerShellIndexExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitInvocation(PowerShellInvocationExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitLabeledStatement(PowerShellLabeledStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitMemberAccess(PowerShellMemberAccessExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitNamedBlock(PowerShellNamedBlockSyntax node) => DefaultVisit(node);
    public virtual TResult VisitParamBlock(PowerShellParamBlockSyntax node) => DefaultVisit(node);
    public virtual TResult VisitParameter(PowerShellParameterSyntax node) => DefaultVisit(node);
    public virtual TResult VisitParenthesizedExpression(PowerShellParenthesizedExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitPowerShellElseClause(PowerShellElseClauseSyntax node) => DefaultVisit(node);
    public virtual TResult VisitPowerShellForStatement(PowerShellForStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitPowerShellFunctionDefinition(PowerShellFunctionDefinitionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitPowerShellIfStatement(PowerShellIfStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitPowerShellLiteral(PowerShellLiteralExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitPowerShellVariable(PowerShellVariableExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitPowerShellWhileStatement(PowerShellWhileStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitScriptBlock(PowerShellScriptBlockSyntax node) => DefaultVisit(node);
    public virtual TResult VisitSubExpression(PowerShellSubExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitSwitchClause(PowerShellSwitchClauseSyntax node) => DefaultVisit(node);
    public virtual TResult VisitSwitchStatement(PowerShellSwitchStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitTernaryExpression(PowerShellTernaryExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitTrapStatement(PowerShellTrapStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitTryStatement(PowerShellTryStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitTypeDefinition(PowerShellTypeDefinitionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitTypeLiteral(PowerShellTypeLiteralSyntax node) => DefaultVisit(node);
    public virtual TResult VisitUnaryExpression(PowerShellUnaryExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitUsingStatement(PowerShellUsingStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitEmbeddedExpression(ShellEmbeddedExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCastExpression(PowerShellCastExpressionSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCmdBlock(CmdParenthesizedBlockSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCmdCall(CmdCallStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCmdElseClause(CmdElseClauseSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCmdFor(CmdForStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCmdGoto(CmdGotoStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCmdIf(CmdIfStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCmdLabel(CmdLabelStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCmdSet(CmdSetStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitCmdVariableReference(CmdVariableReferenceSyntax node) => DefaultVisit(node);
    public virtual TResult VisitEmptyStatement(ShellEmptyStatementSyntax node) => DefaultVisit(node);
    public virtual TResult VisitSkippedText(ShellSkippedTextSyntax node) => DefaultVisit(node);
}
