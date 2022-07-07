using System;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Drawing;
using System.Security.Authentication;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

using MailKit.Net.Smtp;
using MimeKit;

using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;

using AiMaps.Dal;
using AiMaps.Gsqa.Core;
using AiMaps.Gateway.SeleniumControllers;
using AiMaps.Executor;

using AiMaps.Common.Selenium;
using AiMaps.Gateway;
using Gsqa.GHeart.Core.Models;
using Gsqa.GHeart.Core.Models.Performance;
using AiMaps.Common;
using AiMaps.Common.MetricsMonitor;
using Gsqa.Core.Models;
using Gsqa.Core.Models.Rules;
using AiMaps.Common.Selenium.TestHarness;
using FluentAssertions;

namespace GreenSQA.AiMaps.CustomLogic
{
  public class SmartMap
  {
    AiModel thisModel = null;

    //TODO: Razor implementation pending
    //ItemActionPerformer razorActionPerformer = null;

    string logFilePath = string.Empty;

    GSe se = null;

    IWebDriver driver = null;

    public AiModel ThisModel
    {
      get { return thisModel; }
    }

    public long ElapsedMilliseconds
    {
      get { return this.mapExecutor.ElapsedMilliseconds; }
    }

    public string TestSuiteName
    {
      set { JUnitReportHelper.SuiteName = value; }
      get { return JUnitReportHelper.SuiteName; }
    }

    public string TestCaseName
    {
      set { JUnitReportHelper.TestCaseName = value; }
      get { return JUnitReportHelper.TestCaseName; }
    }

    public string TestRunError { get; set; }

    public bool TestRunFailed { get; set; }

    public long RaceElapsedMilliseconds { get; set; }

    public string TestLogsDir
    {
      set { thisModel.ExecutionContext.TestLogsDirectory = value; }
      get { return thisModel.ExecutionContext.TestLogsDirectory; }
    }

    public RealUserMonitoring Rum { get { return this.mapExecutor.Rum; } }

    public FunctionalMonitoring Func { get { return this.mapExecutor.Func; } }

    public string CorrelationString
    {
      get { return this.mapExecutor.CorrelationString; }
      set { this.mapExecutor.CorrelationString = value; }
    }

    private MapExecutor mapExecutor = null;

    public SmartMap(object objMapExecutor)
    {
      mapExecutor = (objMapExecutor as MapExecutor);
      thisModel = (mapExecutor.MapModel as AiModel);
      this.driver = thisModel.SeDriver;
      this.logFilePath = Path.Combine(thisModel.ModelDir, "log.txt");
      //this.razorActionPerformer = new ItemActionPerformer();   
      this.se = new GSe(thisModel.SeDriver, "0");
    }

    public delegate void VoidNonParameterDelegate();

    public void InitRum(bool scanBrowser = true, int rumTimeLimitMilliseconds = 120000)
    {
      if (this.mapExecutor.Rum == null)
      {
        this.mapExecutor.Rum = new RealUserMonitoring(thisModel.SeDriver, thisModel.ExecutionContext, this.mapExecutor.CorrelationString, rumTimeLimitMilliseconds);
      }
      else
      {
        this.mapExecutor.Rum.SeDriver = thisModel.SeDriver;
      }

      this.mapExecutor.Rum.CorrelationString = this.mapExecutor.CorrelationString;
      this.mapExecutor.Rum.ProjectInfo.BotAuthor = thisModel.Map.RobotAuthor;
      this.mapExecutor.Rum.DriverGuid = thisModel.ExecutionContext.DriverGuid;

      if (scanBrowser)
      {
        this.mapExecutor.Rum.ProjectInfo.BrowserName = thisModel.ExecutionContext.WebBrowserName;
        this.mapExecutor.Rum.ProjectInfo.BrowserVersion = thisModel.ExecutionContext.WebBrowserVersion;
        this.mapExecutor.Rum.RunRum();
      }
    }

    public void InitFunc()
    {
      this.mapExecutor.Func = new FunctionalMonitoring();
      this.mapExecutor.Func.CorrelationString = this.mapExecutor.CorrelationString;
      this.mapExecutor.Func.ProjectInfo.BotAuthor = thisModel.Map.RobotAuthor;
    }

    private void SendRumPayload(string transactionName, long excellentMilliSeconds, long toleratingMilliseconds, long duration, string errorReason = null)
    {
      this.mapExecutor.Rum.SendRumTransaction(transactionName, excellentMilliSeconds, toleratingMilliseconds, duration, errorReason, false);
    }

    public void TRum(VoidNonParameterDelegate myTransaction, long excellentMilliSeconds, long toleratingMilliseconds)
    {
      DateTime dtInit = DateTime.Now;
      long elapsedMilliseconds = 0;
      string transactionName = myTransaction.Method.Name;

      try
      {
        myTransaction();
        elapsedMilliseconds = (long)((DateTime.Now - dtInit).TotalMilliseconds);
        SendRumPayload(transactionName, excellentMilliSeconds, toleratingMilliseconds, elapsedMilliseconds, null);
      }
      catch (Exception ex)
      {
        elapsedMilliseconds = (long)((DateTime.Now - dtInit).TotalMilliseconds);
        mapExecutor.Rum.NotifyForStopping();
        Console.WriteLine("ErrorInTransactionRUM: " + transactionName + " - " + ex.Message);
        SendRumPayload(transactionName, excellentMilliSeconds, toleratingMilliseconds, elapsedMilliseconds, ex.Message);
        throw ex;
      }
    }

    public bool TryTRum(VoidNonParameterDelegate myTransaction, long excellentMilliSeconds, long toleratingMilliseconds)
    {
      bool result = true;

      try
      {
        TRum(myTransaction, excellentMilliSeconds, toleratingMilliseconds);
      }
      catch
      {
        result = false;
      }

      return result;
    }

    public void TRumResult(long excellentMilliSeconds, long toleratingSeconds, string errorReason = null)
    {
      this.mapExecutor.Rum.SendRumTransaction("TRESULT", excellentMilliSeconds, toleratingSeconds, this.mapExecutor.ElapsedMs, errorReason, true);
    }

    public void TFuncPublish(long excellentMilliSeconds, long toleratingMilliSeconds, long elapsedMilliseconds, bool sendLog = false)
    {
      var pResult = mapExecutor.Func.Publish(excellentMilliSeconds, toleratingMilliSeconds, elapsedMilliseconds, true, sendLog);

      if (!pResult.IsSuccess)
      {
        Console.WriteLine($"[WARNING] GreenHeart: {pResult.FailureReasonMessage}");
      }
    }

    public void TFunc(long excellentMilliSeconds, long toleratingMilliSeconds, long elapsedMilliseconds, bool isSuccessful, bool sendLog = false)
    {
      var pResult = mapExecutor.Func.Publish(excellentMilliSeconds, toleratingMilliSeconds, elapsedMilliseconds, false, sendLog);

      if (!pResult.IsSuccess)
      {
        Console.WriteLine($"[WARNING] GreenHeart: {pResult.FailureReasonMessage}");
      }
    }

    public void TFunc(VoidNonParameterDelegate myBotLogic, long excellentMilliSeconds, long toleratingMilliseconds, bool sendLog = false)
    {
      TFunc(myBotLogic, null, excellentMilliSeconds, toleratingMilliseconds, sendLog);
    }

    public void TFunc(VoidNonParameterDelegate myBotLogic, string botName, long excellentMilliSeconds, long toleratingMilliseconds, bool sendLog = false)
    {
      long elapsedMilliseconds = 0;
      EvidencesManager.ElapsedTimeTakingEvidences = 0;
      ProjectInfo.SendLog = sendLog;
      DateTime dtInit = DateTime.Now;

      try
      {
        myBotLogic();
        if (!String.IsNullOrEmpty(botName))
        {
          mapExecutor.Func.ProjectInfo.BotName = botName;
        }

        elapsedMilliseconds = (long)(DateTime.Now - dtInit).TotalMilliseconds - EvidencesManager.ElapsedTimeTakingEvidences;
        TFuncPublish(excellentMilliSeconds, toleratingMilliseconds, elapsedMilliseconds, sendLog);
      }
      catch (Exception ex)
      {
        elapsedMilliseconds = (long)(DateTime.Now - dtInit).TotalMilliseconds - EvidencesManager.ElapsedTimeTakingEvidences;
        Console.WriteLine("ErrorInFunctionalBot: " + botName + " - " + ex.Message);
        TFunc(excellentMilliSeconds, toleratingMilliseconds, elapsedMilliseconds, false, sendLog);
        throw ex;
      }
    }

    private MetricsInfoAttribute SetupTestAttributes(VoidNonParameterDelegate myTestLogic)
    {
      MetricsInfoAttribute myMetricInfo = null;
      var atts = myTestLogic.Method.GetCustomAttributes(typeof(MetricsInfoAttribute), true);
      if (atts.Length > 0)
      {
        myMetricInfo = atts.GetValue(0) as MetricsInfoAttribute;
        ProjectInfo.SendLog = true;
      }

      else
      {
        ProjectInfo.SendLog = false;
      }

      if (Func != null)
      {
        this.Func.ProjectInfo.BotName = myTestLogic.Method.Name;
      }

      return myMetricInfo;
    }

    public void RunTest(VoidNonParameterDelegate myTestLogic, VoidNonParameterDelegate beforeTestLogic = null, VoidNonParameterDelegate afterTestLogic = null, long AdditionalMilliseconds = 0)
    {
      MetricsInfoAttribute myMetricInfo = SetupTestAttributes(myTestLogic);
      long elapsedMilliseconds = 0;

      if (myMetricInfo.SkipWhenPreviousTestFailed && this.TestRunFailed)
      {
        return;
      }

      if (Func != null)
      {
        Func.CorrelationString = StringUtility.RandomString(4);
        ThisModel.CorrelationString = Func.CorrelationString;
      }
      JUnitReportHelper.TestCaseName = (thisModel.ExecutionContext.RowNum > 0) ? myTestLogic.Method.Name + "[" + thisModel.ExecutionContext.RowNum + "]" : myTestLogic.Method.Name;
      JUnitReportHelper.ClassName = myTestLogic.Method.DeclaringType.FullName;
      this.TestRunFailed = false;
      this.TestRunError = string.Empty;
      EvidencesManager.ElapsedTimeTakingEvidences = 0;
      DateTime dtInit = DateTime.Now;

      try
      {
        RunBeforeTest(beforeTestLogic);

        Console.WriteLine("[INFO] Running Test [" + JUnitReportHelper.TestCaseName + "]");
        ResetStartTime();
        dtInit = DateTime.Now;
        myTestLogic();

        elapsedMilliseconds = (long)(DateTime.Now - dtInit).TotalMilliseconds + AdditionalMilliseconds - EvidencesManager.ElapsedTimeTakingEvidences;
        JUnitReportHelper.WriteReportData(elapsedMilliseconds, thisModel.ExecutionContext);
        Console.WriteLine("[INFO] Test Passed!");

        RunAfterTest(afterTestLogic, myMetricInfo.TakeScreenshotOnError);

        //This must be after the [beforeTestLogic/myTestLogic/afterTestLogic]
        //so that evidences can go in same payload
        if (myMetricInfo != null && Func != null)
        {
          TFuncPublish(myMetricInfo.ExcellentTime, myMetricInfo.ToleratingTime, elapsedMilliseconds, true);
        }
      }
      catch (Exception ex)
      {
        elapsedMilliseconds = (long)(DateTime.Now - dtInit).TotalMilliseconds + AdditionalMilliseconds - EvidencesManager.ElapsedTimeTakingEvidences;
        this.TestRunFailed = true;
        this.TestRunError = ex.Message;
        Console.WriteLine("[ERROR] " + ex.Message);

        string formatedError = System.Text.RegularExpressions.Regex.Replace(ex.Message, @"\t|\n|\r", "");
        JUnitReportHelper.WriteReportData(elapsedMilliseconds, thisModel.ExecutionContext, formatedError);

        if (myMetricInfo != null && Func != null)
        {
          if (!EvidencesManager.ExistsError(formatedError))
          {
            EvidencesManager.TakeEvidence(thisModel.SeDriver, "Error", formatedError, GetEvidencePath(), myMetricInfo.TakeScreenshotOnError);
          }

          TFunc(myMetricInfo.ExcellentTime, myMetricInfo.ToleratingTime, elapsedMilliseconds, false, true);
        }
      }
      finally
      {
        if (this.TestRunFailed)
        {
          RunAfterTest(afterTestLogic, myMetricInfo.TakeScreenshotOnError);
        }
        Console.WriteLine("[INFO] Test finished");
      }
    }

    private string GetEvidencePath()
    {
      string dirPath = Path.Combine(thisModel.ModelDir, ".tmp");
      Directory.CreateDirectory(dirPath);
      return Path.Combine(dirPath, Guid.NewGuid().ToString() + ".png");
    }

    private bool RunBeforeTest(VoidNonParameterDelegate beforeTestLogic)
    {
      bool result = true;

      try
      {
        if (beforeTestLogic != null)
        {
          Console.WriteLine("[INFO] Running Before Test [" + JUnitReportHelper.TestCaseName + "].[" + beforeTestLogic.Method.Name + "]");
          beforeTestLogic();
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine("[Warning] Before Test [" + JUnitReportHelper.TestCaseName + "].[" + beforeTestLogic.Method.Name + "] failed " + ex.Message);
        string formatedError = beforeTestLogic.Method.Name + ": " + System.Text.RegularExpressions.Regex.Replace(ex.Message, @"\t|\n|\r", "");
        throw new Exception(formatedError);
      }

      return result;
    }

    private bool RunAfterTest(VoidNonParameterDelegate afterTestLogic, bool takeScreenshotOnError)
    {
      bool result = true;

      try
      {
        if (afterTestLogic != null)
        {
          Console.WriteLine("[INFO] Running After Test [" + JUnitReportHelper.TestCaseName + "].[" + afterTestLogic.Method.Name + "]");
          afterTestLogic();
        }
      }
      catch (Exception ex)
      {
        string preFormatedError = System.Text.RegularExpressions.Regex.Replace(ex.Message, @"\t|\n|\r", "");

        if (!EvidencesManager.ExistsError(preFormatedError))
        {
          string formatedError = afterTestLogic.Method.Name + ":" + preFormatedError;
          EvidencesManager.TakeEvidence(thisModel.SeDriver, "AfterTest.Warning", formatedError, GetEvidencePath(), takeScreenshotOnError);
        }

        Console.WriteLine("[Warning] After Test [" + JUnitReportHelper.TestCaseName + "].[" + afterTestLogic.Method.Name + "] failed " + ex.Message);
        result = false;
      }

      return result;
    }

    public bool TryTFunc(VoidNonParameterDelegate myBotLogic, string botName, long excellentMilliSeconds, long toleratingMilliseconds, bool sendLog = false)
    {
      bool result = true;

      try
      {
        TFunc(myBotLogic, botName, excellentMilliSeconds, toleratingMilliseconds, sendLog);
      }
      catch
      {
        result = false;
      }

      return result;
    }

    public bool TryRun(string stepPath)
    {
      return TryRun(stepPath, false);
    }

    public bool TryRun(string stepPath, bool ignoreCode)
    {
      return mapExecutor.TryRunAction(stepPath);
    }

    public string TryRunAll(params string[] actions)
    {
      RunParallel rp = new RunParallel(mapExecutor);
      return rp.TryRunAll(actions);
    }

    public string Race(params string[] actions)
    {
      RunParallel rp = new RunParallel(mapExecutor);
      EvidencesManager.ElapsedTimeTakingEvidences = 0;
      DateTime dtInit = DateTime.Now;
      string winner = rp.Race(actions);
      this.RaceElapsedMilliseconds = (long)(DateTime.Now - dtInit).TotalMilliseconds - EvidencesManager.ElapsedTimeTakingEvidences;

      return winner;
    }

    public string Run(string stepPath)
    {
      Run(stepPath, false);
      return thisModel.GetStep(stepPath).Value.ToString();
    }

    public bool Run(string xPath, bool ignoreCode)
    {
      return mapExecutor.RunAction(xPath, ignoreCode);
    }

    public int RunInt(string stepPath)
    {
      string result = Run(stepPath);
      return System.Convert.ToInt32(result);
    }

    public double RunDouble(string stepPath)
    {
      string result = Run(stepPath);
      return StringUtility.SmartConvertToDouble(result);
    }

    public bool RunBool(string stepPath)
    {
      string result = Run(stepPath);
      return System.Convert.ToBoolean(result);
    }

    public AiStep Step(string stepPath)
    {
      return thisModel.GetStep(stepPath);
    }

    public string StepVal(string stepPath)
    {
      return thisModel.GetStep(stepPath).Value.ToString();
    }

    public string StepValStr(string stepPath)
    {
      return thisModel.GetStep(stepPath).Value.ToString();
    }

    public Int32 StepValInt(string stepPath)
    {
      return System.Convert.ToInt32(thisModel.GetStep(stepPath).Value);
    }

    public Int64 StepValInt64(string stepPath)
    {
      return System.Convert.ToInt64(thisModel.GetStep(stepPath).Value);
    }

    public decimal StepValDecimal(string stepPath)
    {
      return System.Convert.ToDecimal(thisModel.GetStep(stepPath).Value);
    }

    public double StepValDouble(string stepPath)
    {
      return StringUtility.SmartConvertToDouble(thisModel.GetStep(stepPath).Value);
    }

    public object StepValObj(string stepPath)
    {
      return thisModel.GetStep(stepPath).Value;
    }

    public bool StepValBool(string stepPath)
    {
      return System.Convert.ToBoolean(thisModel.GetStep(stepPath).Value);
    }

    public void SetVal(string stepPath, object stepVal)
    {
      //Console.WriteLine("Se debe cambiar la propiedad val del AiStep desde String a tipo object");
      thisModel.GetStep(stepPath).Value = stepVal.ToString();
    }

    private float GetTargetThreshold(AiStep stp)
    {
      float targetThreshold = thisModel.Map.Threshold;
      if (stp.Threshold != 0)
      {
        targetThreshold = (float)stp.Threshold / 100f;
      }
      return targetThreshold;
    }

    public List<string> FindAll(string xPath)
    {
      /*
    string[] parts = xPath.Split(new char[] { '>' });
    AIStage stg = thisModel.GetStage(parts[0]);
    CtrlAction stp = stg.GetStep(parts[1]);

    razorActionPerformer = new ItemActionPerformer(stg.Type, null, null, null,
                  GetTargetThreshold(stp), stp.Timeout,
                  stp.OffestPoint, string.Empty, null,
                  Point.Empty, stp.CustomErrorMessage, stp.MouseMotionParams);

    return thisModel.FindAll(xPath);*/
      Console.WriteLine("Computer vision find all not implemented yet");
      return null;
    }

    public bool RunTap(string elementItem)
    {
      Console.WriteLine("method not implemented yet");
      return true; //razorActionPerformer.Tap(elementItem);
    }

    public bool RunClick(string elementItem)
    {
      Console.WriteLine("method not implemented yet");
      //return razorActionPerformer.Click(elementItem);
      return true;
    }

    public bool RunDoubleClick(string elementItem)
    {
      Console.WriteLine("method not implemented yet");
      //return razorActionPerformer.DoubleClick(elementItem);
      return true;
    }

    public bool RunRightClick(string elementItem)
    {
      Console.WriteLine("method not implemented yet");
      //return razorActionPerformer.RightClick(elementItem);
      return true;
    }

    public bool RunMouseOver(string elementItem)
    {
      Console.WriteLine("method not implemented yet");
      //return razorActionPerformer.Hover(elementItem);
      return true;
    }

    /// <summary>
    /// Send email text
    /// </summary>
    /// <param name="fromEmail">origin email</param>
    /// <param name="toEmail">destination email</param>
    /// <param name="mailSubject">subject</param>
    /// <param name="mailBody">mail body</param>
    /// <param name="clientPort">port of the mail server</param>
    /// <param name="clientHost">host of the mail server</param>
    /// <param name="credentialUserName">user name</param>
    /// <param name="credentialUserPassword">user password</param>
    /// <param name="enableSSL">enable SSL</param>
    public void SendTextEmail(string fromEmail, string toEmail, string mailSubject, string mailBody,
                            int clientPort, string clientHost, string credentialUserName, string credentialUserPassword, bool enableSSL)
    {
      var message = new MimeMessage();
      message.From.Add(new MailboxAddress(fromEmail, fromEmail));
      message.To.Add(new MailboxAddress(toEmail, toEmail));
      message.Subject = mailSubject;
      message.Body = new TextPart("plain")
      {
        Text = mailBody
      };

      using (var client = new SmtpClient())
      {
        client.ServerCertificateValidationCallback = (s, c, h, e) => true;
        client.SslProtocols = SslProtocols.Ssl3 | SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12 | SslProtocols.Tls13;
        client.CheckCertificateRevocation = false;
        client.Connect(clientHost, clientPort, enableSSL);
        // Note: only needed if the SMTP server requires authentication
        client.Authenticate(credentialUserName, credentialUserPassword);
        client.Send(message);
        client.Disconnect(true);
      }
    }

    public bool RunMinimize(string elementItem)
    {
      Console.WriteLine("method not implemented yet");
      return true;
      //return razorActionPerformer.RunMinimizeWindow(elementItem);
    }

    public bool RunMaximize(string elementItem)
    {
      Console.WriteLine("method not implemented yet");
      return true;
      //return razorActionPerformer.RunMaximizeWindow(elementItem);
    }

    //USER_CODE_LOGIC:GLOBAL_CODE
bool IsWarmupOk   = true;

private void SetupFuncMonitor()
{
  InitFunc();
  
  //Setup the project plan data
  Func.ProjectInfo.CustomerId         = StepValStr("Monitoreo>CustomerId");
  Func.ProjectInfo.CustomerName       = StepValStr("Monitoreo>CustomerName");
  Func.ProjectInfo.ProjectId          = StepValStr("Monitoreo>ProjectId");
  Func.ProjectInfo.ProjectName        = StepValStr("Monitoreo>ProjectName");
  Func.ProjectInfo.IterationId        = StepValStr("Monitoreo>IterationId");
  Func.ProjectInfo.TestCaseId         = StepValStr("Monitoreo>TestCaseId") + "_" + StringUtility.RandomString(4); 
  Func.ProjectInfo.ApplicationVersion = StepValStr("Monitoreo>ApplicationVersion");
  Func.ProjectInfo.TechnologyApp      = "Web"; //Change this with the proper value
  
  //Setup the project details
  Func.ProjectInfo.GsqaService           = GsqaService.TIA; //TIA;ATM;Services
  Func.ProjectInfo.HumanRelationRate     = StepValDouble("Monitoreo>HumanRelationRate"); 
  Func.ProjectInfo.DeploymentEnvironment = DevEnvironment.QA; //DEV;QA;STG;PRD
  Func.ProjectInfo.IsGreenHeartPrdMode   = StepValBool("Monitoreo>IsGreenHeartPrdMode");
  
  ResetStartTime();
}

private void EscogerRutaAleatoria()
{
  bool destinoInternacional         = false;
  int aleatorioIda                  = new Random().Next(5, 20);
  int aleatorioRegresoNacional      = aleatorioIda + 3;
  int aleatorioRegresoInternacional = aleatorioIda + 4;
  int rutaNumerador                 = new Random().Next(1, 7);
  string ciudadOrigen               = "";
  string ciudadDestino              = "";
  
  
  switch (rutaNumerador)
  {
   case 1: 
       ciudadOrigen  = "Medellín";
       ciudadDestino = "Cancun";
       destinoInternacional = true;
       break;
   case 2: 
       ciudadOrigen  = "Medellín";
       ciudadDestino = "Cartagena";
       break;
   case 3: 
       ciudadOrigen  = "Medellín";
       ciudadDestino = "Santa Marta";
       break;    
   case 4: 
       ciudadOrigen  = "Medellín";
       ciudadDestino = "Punta Cana";
       destinoInternacional = true;
       break;
   case 5: 
       ciudadOrigen  = "Bogotá";
       ciudadDestino = "Cartagena";
       break; 
   case 6: 
       ciudadOrigen         = "Bogotá";
       ciudadDestino        = "Cancun";
       destinoInternacional = true;
       break;  
   case 7: 
       ciudadOrigen  = "Bogotá";
       ciudadDestino = "Punta Cana";
       destinoInternacional = true;
       break; 
       
   default:
       ciudadOrigen  = "Medellín";
       ciudadDestino = "Cartagena";
       break;
  }
  
  SetVal("BuscarPaqueteVH>EscribirOrigen", ciudadOrigen);  
  SetVal("BuscarPaqueteVH>EscribirDestino", ciudadDestino); 
  SetVal("CalcularFechas>AleatorioIda", aleatorioIda); 
  
  
  if (destinoInternacional == true)
  {
    SetVal("CalcularFechas>AleatorioRegreso", aleatorioRegresoInternacional);   
  }
  else
  {
    SetVal("CalcularFechas>AleatorioRegreso", aleatorioRegresoNacional);  
  }  
     
    
}


public class Calentamiento : IController
{
  AiModel thisModel = null;
  GreenSQA.AiMaps.CustomLogic.SmartMap k;

  System.Collections.Generic.List<string> resultList = null;
  public System.Collections.Generic.List<string> ResultList
  {
    get { return resultList; }
    set { resultList = value; }
  }

  string locationData = string.Empty;
  public string LocationData
  {
    get { return locationData; }
    set { locationData = value; }
  }

  //TODO: OCR not implemented yet
  //public OcrResult OcrResult { get; set; }

  public OpenQA.Selenium.IWebDriver SeDriver { get; set; }

  public string LocatorString { get; set; }

  public Calentamiento(object currentModel, string stageName)
  {
    thisModel = (currentModel as AiModel);
    thisModel.GetStage(stageName).LogicInstance = this;
    if (thisModel.GlobalLogicInstance != null)
    {
      k = (GreenSQA.AiMaps.CustomLogic.SmartMap)thisModel.GlobalLogicInstance;
    }
  }

  //USER_CODE_LOGIC:STAGE_Calentamiento

  
//RunAt:Desktop
bool IController.Execute()
{
  for (int i=1; i<=5; i++)
  { 
    if (!k.TryRun("Calentamiento>NavegaGoogle")) 
    {
      k.IsWarmupOk = false;
      break;  
    }
    
    if (!k.TryRun("Calentamiento>EscribirConsulta")) 
    {
      k.IsWarmupOk = false;
      break;  
    }
  }
  return true;
}
  
}


public class CalcularFechas : IController
{
  AiModel thisModel = null;
  GreenSQA.AiMaps.CustomLogic.SmartMap k;

  System.Collections.Generic.List<string> resultList = null;
  public System.Collections.Generic.List<string> ResultList
  {
    get { return resultList; }
    set { resultList = value; }
  }

  string locationData = string.Empty;
  public string LocationData
  {
    get { return locationData; }
    set { locationData = value; }
  }

  //TODO: OCR not implemented yet
  //public OcrResult OcrResult { get; set; }

  public OpenQA.Selenium.IWebDriver SeDriver { get; set; }

  public string LocatorString { get; set; }

  public CalcularFechas(object currentModel, string stageName)
  {
    thisModel = (currentModel as AiModel);
    thisModel.GetStage(stageName).LogicInstance = this;
    if (thisModel.GlobalLogicInstance != null)
    {
      k = (GreenSQA.AiMaps.CustomLogic.SmartMap)thisModel.GlobalLogicInstance;
    }
  }

  //USER_CODE_LOGIC:STAGE_CalcularFechas

  
//RunAt:Desktop
bool IController.Execute()
{
  return true;
}
  
}


public class ValidarDisponibilidadSitioWeb : IController
{
  AiModel thisModel = null;
  GreenSQA.AiMaps.CustomLogic.SmartMap k;

  System.Collections.Generic.List<string> resultList = null;
  public System.Collections.Generic.List<string> ResultList
  {
    get { return resultList; }
    set { resultList = value; }
  }

  string locationData = string.Empty;
  public string LocationData
  {
    get { return locationData; }
    set { locationData = value; }
  }

  //TODO: OCR not implemented yet
  //public OcrResult OcrResult { get; set; }

  public OpenQA.Selenium.IWebDriver SeDriver { get; set; }

  public string LocatorString { get; set; }

  public ValidarDisponibilidadSitioWeb(object currentModel, string stageName)
  {
    thisModel = (currentModel as AiModel);
    thisModel.GetStage(stageName).LogicInstance = this;
    if (thisModel.GlobalLogicInstance != null)
    {
      k = (GreenSQA.AiMaps.CustomLogic.SmartMap)thisModel.GlobalLogicInstance;
    }
  }

  //USER_CODE_LOGIC:STAGE_ValidarDisponibilidadSitioWeb

  
//RunAt:Desktop
bool IController.Execute()
{
  return true;
}
  
}


public class BuscarPaqueteVH : IController
{
  AiModel thisModel = null;
  GreenSQA.AiMaps.CustomLogic.SmartMap k;

  System.Collections.Generic.List<string> resultList = null;
  public System.Collections.Generic.List<string> ResultList
  {
    get { return resultList; }
    set { resultList = value; }
  }

  string locationData = string.Empty;
  public string LocationData
  {
    get { return locationData; }
    set { locationData = value; }
  }

  //TODO: OCR not implemented yet
  //public OcrResult OcrResult { get; set; }

  public OpenQA.Selenium.IWebDriver SeDriver { get; set; }

  public string LocatorString { get; set; }

  public BuscarPaqueteVH(object currentModel, string stageName)
  {
    thisModel = (currentModel as AiModel);
    thisModel.GetStage(stageName).LogicInstance = this;
    if (thisModel.GlobalLogicInstance != null)
    {
      k = (GreenSQA.AiMaps.CustomLogic.SmartMap)thisModel.GlobalLogicInstance;
    }
  }

  //USER_CODE_LOGIC:STAGE_BuscarPaqueteVH

  
//RunAt:Desktop
bool IController.Execute()
{
  return true;
}
  
}


public class ElegirMejorasVueloVH : IController
{
  AiModel thisModel = null;
  GreenSQA.AiMaps.CustomLogic.SmartMap k;

  System.Collections.Generic.List<string> resultList = null;
  public System.Collections.Generic.List<string> ResultList
  {
    get { return resultList; }
    set { resultList = value; }
  }

  string locationData = string.Empty;
  public string LocationData
  {
    get { return locationData; }
    set { locationData = value; }
  }

  //TODO: OCR not implemented yet
  //public OcrResult OcrResult { get; set; }

  public OpenQA.Selenium.IWebDriver SeDriver { get; set; }

  public string LocatorString { get; set; }

  public ElegirMejorasVueloVH(object currentModel, string stageName)
  {
    thisModel = (currentModel as AiModel);
    thisModel.GetStage(stageName).LogicInstance = this;
    if (thisModel.GlobalLogicInstance != null)
    {
      k = (GreenSQA.AiMaps.CustomLogic.SmartMap)thisModel.GlobalLogicInstance;
    }
  }

  //USER_CODE_LOGIC:STAGE_ElegirMejorasVueloVH

  
//RunAt:Desktop
bool IController.Execute()
{
  return true;
}
  
}


public class VerificarOfertaHotelesVH : IController
{
  AiModel thisModel = null;
  GreenSQA.AiMaps.CustomLogic.SmartMap k;

  System.Collections.Generic.List<string> resultList = null;
  public System.Collections.Generic.List<string> ResultList
  {
    get { return resultList; }
    set { resultList = value; }
  }

  string locationData = string.Empty;
  public string LocationData
  {
    get { return locationData; }
    set { locationData = value; }
  }

  //TODO: OCR not implemented yet
  //public OcrResult OcrResult { get; set; }

  public OpenQA.Selenium.IWebDriver SeDriver { get; set; }

  public string LocatorString { get; set; }

  public VerificarOfertaHotelesVH(object currentModel, string stageName)
  {
    thisModel = (currentModel as AiModel);
    thisModel.GetStage(stageName).LogicInstance = this;
    if (thisModel.GlobalLogicInstance != null)
    {
      k = (GreenSQA.AiMaps.CustomLogic.SmartMap)thisModel.GlobalLogicInstance;
    }
  }

  //USER_CODE_LOGIC:STAGE_VerificarOfertaHotelesVH

  
//RunAt:Desktop
bool IController.Execute()
{
  return true;
}
  
}


public class CompletarReservaPaqueteVH : IController
{
  AiModel thisModel = null;
  GreenSQA.AiMaps.CustomLogic.SmartMap k;

  System.Collections.Generic.List<string> resultList = null;
  public System.Collections.Generic.List<string> ResultList
  {
    get { return resultList; }
    set { resultList = value; }
  }

  string locationData = string.Empty;
  public string LocationData
  {
    get { return locationData; }
    set { locationData = value; }
  }

  //TODO: OCR not implemented yet
  //public OcrResult OcrResult { get; set; }

  public OpenQA.Selenium.IWebDriver SeDriver { get; set; }

  public string LocatorString { get; set; }

  public CompletarReservaPaqueteVH(object currentModel, string stageName)
  {
    thisModel = (currentModel as AiModel);
    thisModel.GetStage(stageName).LogicInstance = this;
    if (thisModel.GlobalLogicInstance != null)
    {
      k = (GreenSQA.AiMaps.CustomLogic.SmartMap)thisModel.GlobalLogicInstance;
    }
  }

  //USER_CODE_LOGIC:STAGE_CompletarReservaPaqueteVH

  
//RunAt:Desktop
bool IController.Execute()
{
  return true;
}
  
}


public class VerificarProcesoCheckoutVH : IController
{
  AiModel thisModel = null;
  GreenSQA.AiMaps.CustomLogic.SmartMap k;

  System.Collections.Generic.List<string> resultList = null;
  public System.Collections.Generic.List<string> ResultList
  {
    get { return resultList; }
    set { resultList = value; }
  }

  string locationData = string.Empty;
  public string LocationData
  {
    get { return locationData; }
    set { locationData = value; }
  }

  //TODO: OCR not implemented yet
  //public OcrResult OcrResult { get; set; }

  public OpenQA.Selenium.IWebDriver SeDriver { get; set; }

  public string LocatorString { get; set; }

  public VerificarProcesoCheckoutVH(object currentModel, string stageName)
  {
    thisModel = (currentModel as AiModel);
    thisModel.GetStage(stageName).LogicInstance = this;
    if (thisModel.GlobalLogicInstance != null)
    {
      k = (GreenSQA.AiMaps.CustomLogic.SmartMap)thisModel.GlobalLogicInstance;
    }
  }

  //USER_CODE_LOGIC:STAGE_VerificarProcesoCheckoutVH

  
//RunAt:Desktop
bool IController.Execute()
{
  return true;
}
  
}


public class Monitoreo : IController
{
  AiModel thisModel = null;
  GreenSQA.AiMaps.CustomLogic.SmartMap k;

  System.Collections.Generic.List<string> resultList = null;
  public System.Collections.Generic.List<string> ResultList
  {
    get { return resultList; }
    set { resultList = value; }
  }

  string locationData = string.Empty;
  public string LocationData
  {
    get { return locationData; }
    set { locationData = value; }
  }

  //TODO: OCR not implemented yet
  //public OcrResult OcrResult { get; set; }

  public OpenQA.Selenium.IWebDriver SeDriver { get; set; }

  public string LocatorString { get; set; }

  public Monitoreo(object currentModel, string stageName)
  {
    thisModel = (currentModel as AiModel);
    thisModel.GetStage(stageName).LogicInstance = this;
    if (thisModel.GlobalLogicInstance != null)
    {
      k = (GreenSQA.AiMaps.CustomLogic.SmartMap)thisModel.GlobalLogicInstance;
    }
  }

  //USER_CODE_LOGIC:STAGE_Monitoreo

  //RunAt:Desktop
bool IController.Execute()
{
  if (k.IsWarmupOk == true)
  {
    k.SetupFuncMonitor();
    k.TestSuiteName = "Reservar_Paquete_Basico_VH";
    k.EscogerRutaAleatoria();
  
    k.RunTest(A_ValidarDisponibilidadSitioWeb, null, A_AfterValidarDisponibilidadSitioWeb);
    k.RunTest(B_BuscarPaqueteVH, B_BeforeBuscarPaqueteVH, B_AfterBuscarPaqueteVH);
    C_ElegirMejorasVueloVH__AND__D_VerificarOfertaHotelesVH();
    k.RunTest(E_CompletarReservaPaqueteVH, null, E_AfterCompletarReservaPaqueteVH);
    k.RunTest(F_VerificarProcesoCheckoutVH, F_BeforeVerificarProcesoCheckoutVH);
  }
  
  k.Run("Monitoreo>KillDriver");
  return true;
}
 

[MetricsInfo(ExcellentTime = 3000,  ToleratingTime = 5000, SkipWhenPreviousTestFailed = true)]
private void A_ValidarDisponibilidadSitioWeb()
{
  k.Run("ValidarDisponibilidadSitioWeb>Navegar");
  k.Run("ValidarDisponibilidadSitioWeb>EsperarCargaPagina");
}

private void A_AfterValidarDisponibilidadSitioWeb()
{
  k.Run("ValidarDisponibilidadSitioWeb>Esperar");
}

private void B_BeforeBuscarPaqueteVH()
{
  k.Run("CalcularFechas", true);
  k.Run("BuscarPaqueteVH>EscribirOrigen");
  k.Run("BuscarPaqueteVH>SeleccionarOrigen");
  k.Run("BuscarPaqueteVH>EscribirDestino");
  k.Run("BuscarPaqueteVH>SeleccionarDestino");
  
  if (!k.TryRun("BuscarPaqueteVH>SeleccionarFechaSalida"))
  {
    k.Run("BuscarPaqueteVH>ClicFechaSalida");
    k.Run("BuscarPaqueteVH>SeleccionarFechaSalida");
  }
  
  if (!k.TryRun("BuscarPaqueteVH>SeleccionarFechaRegreso"))
  {
    k.Run("BuscarPaqueteVH>ClicFechaRegreso");
    k.Run("BuscarPaqueteVH>SeleccionarFechaRegreso");
  }
 
  k.Run("BuscarPaqueteVH>SeleccionarPasajeros");
  k.Run("BuscarPaqueteVH>RestarNumeroPasajeros");
  k.Run("BuscarPaqueteVH>ClicConfirmarPasajeros");
}

[MetricsInfo(ExcellentTime = 15000,  ToleratingTime = 20000, SkipWhenPreviousTestFailed = true)]
private void B_BuscarPaqueteVH()
{
  k.Run("BuscarPaqueteVH>BuscarPaquete");
  k.Run("BuscarPaqueteVH>LogFechas");
  k.Run("Monitoreo>LogIniciaPrueba");
  k.TryRun("BuscarPaqueteVH>EsperarResultados");
  k.Run("BuscarPaqueteVH>EsperarResultadosPaquete");
  k.Run("Monitoreo>LogFinalizaPrueba");
}

private void B_AfterBuscarPaqueteVH()
{
  k.Run("BuscarPaqueteVH>CrearElementoSearchId");
  k.Run("BuscarPaqueteVH>ObtenerSearchId");
  k.Run("Monitoreo>LogSearchId");
}

private void C_ElegirMejorasVueloVH__AND__D_VerificarOfertaHotelesVH()
{
  k.Run("ElegirMejorasVueloVH>ScrollSeleccionarVuelo");
  k.Run("ElegirMejorasVueloVH>EsperarSeleccionarVuelo");
  k.Run("ElegirMejorasVueloVH>SeleccionarVuelo");
  
  string accionGanadora = k.Race("ElegirMejorasVueloVH>IntentarEsperarOpcionesVuelo","VerificarOfertaHotelesVH>IntentarEsperarHoteles");
  
  if (accionGanadora == "ElegirMejorasVueloVH>IntentarEsperarOpcionesVuelo")
  {
    k.RunTest(C_ElegirMejorasVueloVH, null, null, AdditionalMilliseconds:k.RaceElapsedMilliseconds);
    k.RunTest(D_VerificarOfertaHotelesVH, D_BeforeVerificarOfertaHotelesVH);
  }
  else
  {
    k.RunTest(D_VerificarOfertaHotelesVH, null, null, AdditionalMilliseconds:k.RaceElapsedMilliseconds); 
  }
}

[MetricsInfo(ExcellentTime = 3000,  ToleratingTime = 5000, SkipWhenPreviousTestFailed = true)]
private void C_ElegirMejorasVueloVH()
{
  k.Run("Monitoreo>LogIniciaPrueba");
  k.Run("ElegirMejorasVueloVH>EsperarOpcionesVuelo");
  k.Run("Monitoreo>LogFinalizaPrueba");
  k.Run("Monitoreo>LogSearchId");
}

private void D_BeforeVerificarOfertaHotelesVH()
{
  k.Run("VerificarOfertaHotelesVH>ScrollContinuar");
  string ganador = k.Race("VerificarOfertaHotelesVH>CerrarSpam", "VerificarOfertaHotelesVH>IntentarContinuar" );
  
  if (ganador == "VerificarOfertaHotelesVH>CerrarSpam")
  {
    k.Run("VerificarOfertaHotelesVH>Continuar");
  }
}

[MetricsInfo(ExcellentTime = 5000,  ToleratingTime = 10000, SkipWhenPreviousTestFailed = true)]
private void D_VerificarOfertaHotelesVH()
{
  k.Run("Monitoreo>LogIniciaPrueba");
  k.Run("VerificarOfertaHotelesVH>EsperarHoteles");
  k.Run("Monitoreo>LogFinalizaPrueba");
  k.Run("Monitoreo>LogSearchId");
}

[MetricsInfo(ExcellentTime = 5000,  ToleratingTime = 10000, SkipWhenPreviousTestFailed = true)]
private void E_CompletarReservaPaqueteVH()
{
  k.Run("CompletarReservaPaqueteVH>Comprar");
  k.Run("Monitoreo>LogIniciaPrueba");
  k.Run("CompletarReservaPaqueteVH>EsperaInteractVerifReserva");
  k.Run("CompletarReservaPaqueteVH>EsperarCompletarReserva");
  k.Run("Monitoreo>LogFinalizaPrueba");
  k.Run("Monitoreo>LogSearchId");
}

private void E_AfterCompletarReservaPaqueteVH()
{
  k.Run("CompletarReservaPaqueteVH>ClicSr");
  k.Run("CompletarReservaPaqueteVH>EscribirNombre");
  k.Run("CompletarReservaPaqueteVH>EscribirApellido");
  k.Run("CompletarReservaPaqueteVH>EscribirIdentificacion");
  k.Run("CompletarReservaPaqueteVH>SeleccionarDiaNacimiento");
  k.Run("CompletarReservaPaqueteVH>SelecionarMesNacimiento");
  k.Run("CompletarReservaPaqueteVH>SeleccionarAnoNacimiento");
  k.Run("CompletarReservaPaqueteVH>EscribirCorreo");
  k.Run("CompletarReservaPaqueteVH>EscribirTelefono");
  k.Run("CompletarReservaPaqueteVH>ScrollCondicionesEspeciales");
  k.Run("CompletarReservaPaqueteVH>EvidenciaSinCondicionesEspeciales");
}

private void F_BeforeVerificarProcesoCheckoutVH()
{
  k.Run("VerificarProcesoCheckoutVH>ScrollAutorizar");
  k.Run("VerificarProcesoCheckoutVH>EsperarAutorizar");
  k.Run("VerificarProcesoCheckoutVH>ClicAutorizar");
  k.Run("VerificarProcesoCheckoutVH>EsperarContinuar");
}

[MetricsInfo(ExcellentTime = 10000,  ToleratingTime = 15000, SkipWhenPreviousTestFailed = true)]
private void F_VerificarProcesoCheckoutVH()
{
  k.Run("VerificarProcesoCheckoutVH>ClicContinuar");
  k.Run("Monitoreo>LogIniciaPrueba");
  k.TryRun("VerificarProcesoCheckoutVH>EsperarResultados");
  k.Run("VerificarProcesoCheckoutVH>EsperarItinerario");
  k.Run("Monitoreo>LogFinalizaPrueba");
  k.Run("Monitoreo>LogSearchId");
}
  
}
    /// <summary>
    /// Writes a line to given file path
    /// </summary>
    /// <param name="filePath">file path</param>
    /// <param name="textLine">line to write</param>
    /// <param name="append">a boolean indicating if appends the file or overwrites it</param>
    public void WriteLineToFile(string filePath, string textLine, bool append)
    {
      for (int numTries = 0; numTries <= 5; numTries++)
      {
        try
        {
          if (append)
          {
            File.AppendAllText(filePath, textLine + System.Environment.NewLine);
          }
          else
          {
            File.WriteAllText(filePath, textLine + System.Environment.NewLine);
          }

          break;
        }
        catch
        {
          if (numTries == 5)
          {
            throw;
          }
          System.Threading.Thread.Sleep(300);
        }
      }
    }

    public void LogFile(string textLine)
    {
      WriteLineToFile(logFilePath, textLine, true);
    }

    public void LogFile(string filePath, string textLine)
    {
      WriteLineToFile(filePath, textLine, true);
    }

    public void LogFile(string filePath, string textLine, bool append)
    {
      WriteLineToFile(filePath, textLine, append);
    }

    public void WriteLineToFile(string textLine)
    {
      WriteLineToFile(logFilePath, textLine, true);
    }

    public void WriteLineToFile(string textLine, bool append)
    {

      WriteLineToFile(logFilePath, textLine, append);
    }

    public void Message(string messageText, string messageTittle)
    {
      Console.WriteLine($"The message window for [{messageText}] with tittle [{messageTittle}] is not implemented yet ");
    }

    public void Message(string messageText)
    {
      Console.WriteLine($"The message window for [{messageText}] is not implemented yet ");
    }

    public void Message(int someNumber)
    {
      Console.WriteLine($"The message window for [{someNumber}] is not implemented yet ");
    }

    public void Sleep(int milliseconds)
    {
      System.Threading.Thread.Sleep(milliseconds);
    }

    public void ResetStartTime()
    {
      mapExecutor.StartTime = DateTime.Now;
      JUnitReportHelper.SuiteBegins = DateTime.Now;
    }

  }
}