using AiMaps.Gateway;
using AiMaps.Gsqa.Core;

public class __STAGE_NAME__ : IController
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

  public __STAGE_NAME__(object currentModel, string stageName)
  {
    thisModel = (currentModel as AiModel);
    thisModel.GetStage(stageName).LogicInstance = this;
    if (thisModel.GlobalLogicInstance != null)
    {
      k = (GreenSQA.AiMaps.CustomLogic.SmartMap)thisModel.GlobalLogicInstance;
    }
  }

  //USER_CODE_LOGIC:STAGE___STAGE_NAME__

  //#LOGIC_METHOD#
  bool IController.Execute() { return true; }
}