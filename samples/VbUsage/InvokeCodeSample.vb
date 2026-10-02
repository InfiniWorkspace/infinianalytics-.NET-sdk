Imports System
Imports InfiniAnalytics

Public Module InvokeCodeSample

    ' Same code a UiPath Invoke Code activity would contain.
    Public Function Run(token As String, automationId As String) As String
        Dim client As New InfiniAnalyticsClient(token)
        Dim execution = client.StartExecution(automationId, description:="Inicio del proceso")
        Try
            execution.Event("Facturas descargadas")
            execution.Warning("Factura sin fecha, se usa la de hoy")
        Catch ex As Exception
            execution.Error("Fallo procesando facturas", ex, errorId:="E001")
        End Try
        execution.End("Fin del proceso")
        Return execution.ExecutionId
    End Function

    ' A later Invoke Code that only receives the ids as arguments.
    Public Sub EndStartedExecution(token As String, automationId As String, executionId As String)
        Dim execution As New Execution(New InfiniAnalyticsClient(token), automationId, executionId)
        execution.End("Fin del proceso")
    End Sub

End Module
