using System;
using System.Collections.Generic;
using System.Linq;

using Micros.Ops;
using Micros.Ops.Extensibility;
using Micros.PosCore.Extensibility;
using Micros.PosCore.Extensibility.Ops;
using System.Reflection;
using System.Globalization;
using System.Threading;
using System.Xml;
using System.IO;
using System.Text;
using static Micros.PosCore.DataStore.DbRecords.DbKey;
using System.Collections;
//using TWS.Licensing;
using TWS.Simphony.Helpers;
using TWS.Prisma;
using bp.simphony.payment;

namespace TWS.Simphony.MP.Payment
{
    /// <summary>
    /// Implements the extension application
    /// </summary>
    public class PrismaInterfaceExtApp : OpsExtensibilityApplication
    {
        private static readonly NLog.Logger LOG = NLog.LogManager.GetCurrentClassLogger();

        //FOR FUTURE USE
        //private bool mLicensed = false;        
        //private string mHardID = "";

        #region Members
        private bool mTenderTriggeredByInterface = false;
        private Dictionary<string, Dictionary<int, string>> mCardPlanMap = new Dictionary<string, Dictionary<int, string>>();
        private List<ISSUER_OUT> mCardDefList = new List<ISSUER_OUT>();
        #endregion Members

        public PrismaInterfaceExtApp(IExecutionContext context)
        : base(context)
        {
            try
            {            
                this.OpsInitEvent += OnOpsInitEvent;
                this.OpsTmedPreviewEvent += OnOpsTmedPreviewEvent;
                this.OpsTransactionCancelEvent += OnOpsTransactionCancelEvent;
                this.OpsErrorMessageEvent += OnOpsErrorMessageEvent;
         
                string path = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().ManifestModule.FullyQualifiedName) + "\\";
                ConfigMgr.Instance.Initialize(path + "\\bp.payment.config");

                if (ConfigMgr.Instance.ShowIFCVersion)
                    OpsContext.ShowMessage("PRISMA PAYMENT Version: " + this.GetType().Assembly.GetName().Version.ToString());

                LOG.Info("{Message}", "PRISMA PAYMENT Version: " + this.GetType().Assembly.GetName().Version.ToString());

                mCardPlanMap = GetCCPlanDef();

                IntegratedPOS.SetPortParameters(
                    new COM_PARAMS()
                    {
                        COMName = "COM" + ConfigMgr.Instance.LAPOSSerialPortNumber,
                        BaudRate = UInt16.Parse(ConfigMgr.Instance.LAPOSSerialPortBaudRate),
                        Parity = (byte)(ConfigMgr.Instance.LAPOSSerialPortParity.Length == 1 ? ConfigMgr.Instance.LAPOSSerialPortParity[0] : 'N'),
                        ByteSize = UInt16.Parse(ConfigMgr.Instance.LAPOSSerialPortByteSize),
                        StopBits = UInt16.Parse(ConfigMgr.Instance.LAPOSSerialPortStopBits),
                    });

                string errMsg = "";
                if (IntegratedPOS.GetIssuer(out mCardDefList, ref errMsg) != VPI_ERROR_CODE.VPI_OK)
                    LOG.Error("{Message}", $"Error reading card list from POS\r\n{errMsg}");

                //LOG.Instance.LogEvent("MP QR PAYMENT Version: " + this.GetType().Assembly.GetName().Version.ToString(), TWS.Log.Logger.VERBOSITY_LEVEL.INFO);

                /* FOR FUTURE USE
                //licensing
                LicenseMgr.Instance.Initialize(path, path, "TWS.Simphony.MP.Payment.dll", false);

                mHardID =   HardwareIdReader.GetHash(OpsContext.Product + "_" +
                            OpsContext.LocationId + "_" +
                            OpsContext.PropertyID + "_" +
                            OpsContext.WorkstationID);

                mLicensed = LicenseMgr.Instance.ValidateLicense("TWS.Simphony.MP.Payment.dll", mHardID, true);*/
            }
            catch (Exception ex)
            {
                OpsContext.ShowException(ex, "Exception creating MPQRInterfaceExtApp");
                throw ex;
            }
        }

        #region Extensibility Keys
        [ExtensibilityMethod]
        public void CreditCardPaymentEvent()
        {
            BeginPayment(0.00m, 0);
        }

        [ExtensibilityMethod]
        public void CreditCardPaymentVoidEvent()
        {
            VPI_ERROR_CODE posResp;
            string errMsg = "";

            long voucherNum = OpsContext.RequestNumericEntry("Ingrese Nº de Cupón del Voucher de Tarjeta", "Cancelación de Pago con Tarjeta") ?? 0;

            if (voucherNum == 0)
                return;

            CARDDATA_OUT cardData;

            cardData.CardCode = SelectCardFromList();

            if (cardData.CardCode == "")
            {
                OpsContext.ShowMessage("Se obtendrá el tipo de tarjeta.\r\nInserte o deslice la tarjeta cuando el POS lo solicite.\r\n\r\nCierre este diálogo para continuar.");

                posResp = IntegratedPOS.GetCardData(out cardData, ref errMsg);

                if (posResp != VPI_ERROR_CODE.VPI_OK)
                {
                    posResp = IntegratedPOS.ClosePort(ref errMsg);
                    OpsContext.ShowError($"Error leyendo tipo de tarjeta.\r\n{errMsg}");
                    LOG.Error("{Message}", $"Error reading card data: [{posResp}] - {errMsg}");
                    return;
                }

                OpsContext.ShowMessage("Remueva la tarjeta cierre este diálogo para continuar y siga las instrucciones desde el POS");
            }

            VOID_IN voidParam = new VOID_IN()
            {
                IssuerCode = cardData.CardCode.TrimStart('0'),
                OriginalTicket = $"{voucherNum}",
                CUIT = ConfigMgr.Instance.LAPOSMerchantCUIT,
                MerchantName = ConfigMgr.Instance.LAPOSMerchantName,
            };

            TRX_OUT trxOut;

            //OpsContext.ShowMessage("Inserte o deslice la tarjeta cuando el POS lo solicite.\r\n\r\nCierre este diálogo para continuar");
            if ((posResp = IntegratedPOS.PurchaseVoid(voidParam, out trxOut, ref errMsg)) != VPI_ERROR_CODE.VPI_OK)
            {
                OpsContext.ShowError($"Error al intentar anular la venta.\r\n{errMsg}");
                LOG.Error("{Message}", $"LAPOS Purchase Void operation failed:\r\n{trxOut.DumpString()}");
                return;
            }

            OpsContext.ShowMessage("Espere a finalizar las dos impresiones de vouchers, y retire la tarjeta en caso de que el POS lo solicite.\r\n\r\nLuego acepte para continuar.");

            if (ConfigMgr.Instance.LAPOSShowPaymentResultDialog)
                OpsContext.ShowMessage(trxOut.DumpString());

            int retries = 0;
            do
            {
                posResp = IntegratedPOS.TestConnection(ref errMsg);

                if (posResp != VPI_ERROR_CODE.VPI_OK)
                {
                    Thread.Sleep(ConfigMgr.Instance.LAPOSWaitMs / 2);
                    if (retries++ > 10)
                        OpsContext.ShowError("Verifique que no haya quedado una tarjeta insertada. Si el error persiste, reinicie el POS");
                }
            } while (posResp != VPI_ERROR_CODE.VPI_OK);
        }

        [ExtensibilityMethod]
        public void CreditCardRefundEvent()
        {
            VPI_ERROR_CODE retCode;
            int instalment = 1;

            VPI_ERROR_CODE posResp;
            string errMsg = "";

            decimal amount = OpsContext.RequestAmountEntry("Ingrese monto a devolver", "Devolución de Pago con Tarjeta") ?? 0.00m;
            long voucherNum = OpsContext.RequestNumericEntry("Ingrese Nº de Cupón del Voucher de Tarjeta", "Devolución de Pago con Tarjeta") ?? 0;
            string date = OpsContext.RequestAlphaEntry("Ingrese fecha del Voucher de Tarjeta (dd/mm/yyyy)", "Devolución de Pago con Tarjeta") ?? "";

            if (amount == 0.00m || date.Trim() == "")
            {
                OpsContext.ShowError("Se deben completar los 3 campos requeridos");
                return;
            }

            if (!DateTime.TryParseExact(date.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                OpsContext.ShowError("Fecha ingresada inválida.");
                return;
            }

            CARDDATA_OUT cardData;
            cardData.CardCode = SelectCardFromList();

            if (cardData.CardCode == "")
            {
                OpsContext.ShowMessage("Se obtendrá el tipo de tarjeta.\r\nInserte o deslice la tarjeta cuando el POS lo solicite.\r\n\r\nCierre este diálogo para continuar.");

                posResp = IntegratedPOS.GetCardData(out cardData, ref errMsg);

                if (posResp != VPI_ERROR_CODE.VPI_OK)
                {
                    posResp = IntegratedPOS.ClosePort(ref errMsg);
                    OpsContext.ShowError($"Error leyendo tipo de tarjeta.\r\n{errMsg}");
                    LOG.Error("{Message}", $"Error reading card data: [{posResp}] - {errMsg}");
                    return;
                }

                OpsContext.ShowMessage("Remueva la tarjeta cierre este diálogo para continuar y siga las instrucciones desde el POS");
            }

            REFUND_IN refundParam = new REFUND_IN()
            {
                IssuerCode = cardData.CardCode.TrimStart('0'),
                Amount = $"{(int)(amount * 100.0m)}",
                OriginalTicket = $"{voucherNum}",
                OriginalDate = date.Trim(),
                PlanCode = GetCardPlan(cardData.CardCode.TrimStart('0'), instalment),
                ReceiptNumber = "",
                MerchantCode = ConfigMgr.Instance.LAPOSMerchantCode,
                InstalmentCount = instalment.ToString(),
                CUIT = ConfigMgr.Instance.LAPOSMerchantCUIT,
                MerchantName = ConfigMgr.Instance.LAPOSMerchantName,
                Linemode = (char)1,
            };

            TRX_OUT trxOut;      
            if ((retCode = IntegratedPOS.Refund(refundParam, out trxOut, ref errMsg)) != VPI_ERROR_CODE.VPI_OK)
            {
                OpsContext.ShowError($"Error al intentar la devolución.\r\n{errMsg}");
                LOG.Error("{Message}", $"LAPOS Refund operation failed:\r\n{trxOut.DumpString()}");
                return;
            }

            OpsContext.ShowMessage("Espere a finalizar las dos impresiones de vouchers, y retire la tarjeta en caso de que el POS lo solicite.\r\n\r\nLuego acepte para continuar.");

            if (ConfigMgr.Instance.LAPOSShowPaymentResultDialog)
                OpsContext.ShowMessage(trxOut.DumpString());

            int retries = 0;
            do
            {
                retCode = IntegratedPOS.TestConnection(ref errMsg);

                if (retCode != VPI_ERROR_CODE.VPI_OK)
                {
                    Thread.Sleep(ConfigMgr.Instance.LAPOSWaitMs / 2);
                    if (retries++ > 10)
                        OpsContext.ShowError("Verifique que no haya quedado una tarjeta insertada. Si el error persiste, reinicie el POS");
                }
            } while (retCode != VPI_ERROR_CODE.VPI_OK);
        }

        [ExtensibilityMethod]
        public void CreditCardRefundVoidEvent()
        {
            VPI_ERROR_CODE posResp;
            string errMsg = "";

            long voucherNum = OpsContext.RequestNumericEntry("Ingrese Nº de Cupón del Voucher de Tarjeta", "Cancelación de Pago con Tarjeta") ?? 0;

            if (voucherNum == 0)
                return;

            OpsContext.ShowMessage("Se obtendrá el tipo de tarjeta.\r\nInserte o deslice la tarjeta cuando el POS lo solicite.\r\n\r\nCierre este diálogo para continuar.");

            CARDDATA_OUT cardData;
            posResp = IntegratedPOS.GetCardData(out cardData, ref errMsg);

            if (posResp != VPI_ERROR_CODE.VPI_OK)
            {
                posResp = IntegratedPOS.ClosePort(ref errMsg);
                OpsContext.ShowError($"Error leyendo tipo de tarjeta.\r\n{errMsg}");
                LOG.Error("{Message}", $"Error reading card data: [{posResp}] - {errMsg}");
                return;
            }

            OpsContext.ShowMessage("Remueva la tarjeta cierre este diálogo para continuar y siga las instrucciones desde el POS");

            VOID_IN voidParam = new VOID_IN()
            {
                IssuerCode = cardData.CardCode.TrimStart('0'),
                OriginalTicket = $"{voucherNum}",
                CUIT = ConfigMgr.Instance.LAPOSMerchantCUIT,
                MerchantName = ConfigMgr.Instance.LAPOSMerchantName,
            };

            TRX_OUT trxOut;

            //OpsContext.ShowMessage("Inserte o deslice la tarjeta cuando el POS lo solicite.\r\n\r\nCierre este diálogo para continuar");
            if ((posResp = IntegratedPOS.RefundVoid(voidParam, out trxOut, ref errMsg)) != VPI_ERROR_CODE.VPI_OK)
            {
                OpsContext.ShowError($"Error al intentar anular la devolución.\r\n{errMsg}");
                LOG.Error("{Message}", $"LAPOS Refund Void operation failed:\r\n{trxOut.DumpString()}");
                return;
            }

            OpsContext.ShowMessage("Espere a finalizar las dos impresiones de vouchers, y retire la tarjeta en caso de que el POS lo solicite.\r\n\r\nLuego acepte para continuar.");

            if (ConfigMgr.Instance.LAPOSShowPaymentResultDialog)
                OpsContext.ShowMessage(trxOut.DumpString());

            int retries = 0;
            do
            {
                posResp = IntegratedPOS.TestConnection(ref errMsg);

                if (posResp != VPI_ERROR_CODE.VPI_OK)
                {
                    Thread.Sleep(ConfigMgr.Instance.LAPOSWaitMs / 2);
                    if (retries++ > 10)
                        OpsContext.ShowError("Verifique que no haya quedado una tarjeta insertada. Si el error persiste, reinicie el POS");
                }
            } while (posResp != VPI_ERROR_CODE.VPI_OK);
        }

        [ExtensibilityMethod]
        public void CreditCardBatchCloseEvent()
        {
            VPI_ERROR_CODE retCode;
            BATCHCLOSE_OUT batchOut;
            string errMsg = "";

            if ((retCode = IntegratedPOS.BatchClose(out batchOut, ref errMsg)) != VPI_ERROR_CODE.VPI_OK)
            {
                OpsContext.ShowError($"Error al intentar Cerrar el Lote.\r\n{errMsg}");
                LOG.Error("{Message}", $"LAPOS Batch Close operation failed:\r\n{batchOut.DumpString()}");
                return;
            }

            OpsContext.ShowMessage(batchOut.DumpString());
        }

        [ExtensibilityMethod]
        public void CreditCardPrintBatchCloseEvent()
        {
            VPI_ERROR_CODE retCode;
            string errMsg = "";

            if ((retCode = IntegratedPOS.PrintBatchClose(ref errMsg)) != VPI_ERROR_CODE.VPI_OK)
            {
                OpsContext.ShowError($"Error al intentar Imprimir el Cirre de Lote\r\n{errMsg}");
                LOG.Error("{Message}", $"LAPOS Print Batch Close operation failed:\r\n{errMsg}");
            }
            else
                OpsContext.ShowMessage("Operación exitosa.\r\n");
        }

        [ExtensibilityMethod]
        public void CreditCardPrintTicketEvent()
        {
            VPI_ERROR_CODE retCode;
            string errMsg = "";

            if ((retCode = IntegratedPOS.PrintTicket(ref errMsg)) != VPI_ERROR_CODE.VPI_OK)
            {
                OpsContext.ShowError($"Error al intentar Imprimir el Ticket\r\n{errMsg}");
                LOG.Error("{Message}", $"LAPOS Print Ticket operation failed:\r\n{errMsg}");
            }
            else
                OpsContext.ShowMessage("Operación exitosa.\r\n");
        }

        [ExtensibilityMethod]
        public void CreditCardGetBatchCloseData()
        {
            VPI_ERROR_CODE retCode;
            string errMsg = "";
            List<BATCHCLOSEDATA_OUT> batchList;

            if ((retCode = IntegratedPOS.GetBatchCloseData(out batchList, ref errMsg)) != VPI_ERROR_CODE.VPI_OK)
            {
                OpsContext.ShowError($"Error al intentar obtener los totales de Cierre de Lote\r\n{errMsg}");
                LOG.Error("{Message}", $"LAPOS Get Batch Close Data operation failed:\r\n{errMsg}");
                return;
            }

            OpsContext.ShowTextList("Cierre de Lote", batchList.Select(batch=> batch.DumpString()));
        }

        [ExtensibilityMethod]
        public void CreditCardGetPlanEvent()
        {
            DoCCGetPlan();
        }

        [ExtensibilityMethod]
        public void CreditCardGetPOSCardsEvent()
        {
            DoCCGetCards();
        }
        #endregion Extensibility Keys

        #region LAPOS Helper Methods
        private bool BeginPayment(decimal amount_, int tndrObjNum_)
        {
            LOG.Debug("ENTER");

            bool retVal = false;
            string errMsg = "";
            string ccCode = "";
            VPI_ERROR_CODE posResp;

            bool fromTender = (tndrObjNum_ != 0); 

            try
            {
                //ask for amount if no amount is received
                if (amount_ == 0.00m)
                    amount_ = OpsContext.RequestAmountEntry("Ingrese monto a abonar", "Pago con Tarjeta", OpsContext.Check.TotalDue) ?? 0.00m;

                if (amount_ == 0.00m)
                    return retVal;

                string tip = "0";
                if (amount_ > OpsContext.Check.TotalDue)
                    if (OpsContext.AskQuestion($"El monto sobrepasa el total de la cuenta.\r\n¿Desea utilizar los ${(amount_ - OpsContext.Check.TotalDue): N2} como propina?"))
                        tip = $"{OpsContext.Check.TotalDue - amount_: N2}";

                //Find Credit Card Code (if we came from a Tender Media key)
                if (tndrObjNum_ != 0)
                {
                    if (ConfigMgr.Instance.PrismaCCToSimphonyObjNumMap.Values.Contains(tndrObjNum_))
                        ccCode = ConfigMgr.Instance.PrismaCCToSimphonyObjNumMap.Single(p => p.Value == tndrObjNum_).Key;
                    else
                    {
                        OpsContext.ShowError($"El medio de pago {tndrObjNum_} no está asociado a ninguna tarjeta de crédito en la configuración");
                        LOG.Error("{Message}", $"Couldn't find Tender Media Obj Num {tndrObjNum_} mapped to a PRISMA Credit Card");
                        return retVal;
                    }
                }

                //Test LAPOS Device Status
                posResp = TestLAPOSDevice(ref errMsg);

                //Get Card Type (if we came from Extensibility Key or tender not mapped properly)
                if (ccCode == "")
                {
                    OpsContext.ShowMessage("Se obtendrá el tipo de tarjeta.\r\nInserte o deslice la tarjeta cuando el POS lo solicite.\r\n\r\nCierre este diálogo para continuar.");

                    CARDDATA_OUT cardData;
                    posResp = IntegratedPOS.GetCardData(out cardData, ref errMsg);

                    if (posResp != VPI_ERROR_CODE.VPI_OK)
                    {
                        posResp = IntegratedPOS.ClosePort(ref errMsg);
                        OpsContext.ShowError($"Error leyendo tipo de tarjeta.\r\n{errMsg}");
                        LOG.Error("{Message}", $"Error reading card data: [{posResp}] - {errMsg}");
                        return retVal;
                    }

                    ccCode = cardData.CardCode.TrimStart('0');
                    OpsContext.ShowMessage("Remueva la tarjeta y cierre este diálogo para continuar");
                }

                //Find Card obj num in Simphony (if we came from Extensibility Key)
                if (tndrObjNum_ == 0)
                {
                    if (!ConfigMgr.Instance.PrismaCCToSimphonyObjNumMap.TryGetValue(ccCode, out tndrObjNum_))
                    {
                        OpsContext.ShowError($"No se encuentra definida la tarjeta {ccCode} en la configuración");
                        LOG.Error("{Message}", $"Couldn't find PRISMA Card [{ccCode}] mapped to a Simphony tender Obj Num");
                        return retVal;
                    }
                }

                //Send payment order to pinpad
                int instalment = 1;
                PURCHASE_IN purchaseData = new PURCHASE_IN()
                {
                    IssuerCode = ccCode,
                    Amount = $"{ (int)(amount_ * 100.0m) }",
                    Tip = tip,
                    CUIT = ConfigMgr.Instance.LAPOSMerchantCUIT,
                    MerchantCode = ConfigMgr.Instance.LAPOSMerchantCode,
                    ReceiptNumber = $"{OpsContext.CheckNumber}",
                    Linemode = (char)0x01,
                    InstalmentCount = $"{instalment}",
                    PlanCode = GetCardPlan(ccCode, instalment),
                    MerchantName = ConfigMgr.Instance.LAPOSMerchantName,
                };

                OpsContext.ShowMessage("Inserte o deslice la tarjeta cuando el POS lo solicite.\r\n\r\nCierre este diálogo para continuar.");

                TRX_OUT trxData;
                posResp = IntegratedPOS.Purchase(purchaseData, out trxData, ref errMsg);

                if (posResp != VPI_ERROR_CODE.VPI_OK || Int16.Parse(trxData.HostRespCode) != 0)
                {
                    OpsContext.ShowError($"Error en la operación de pago.\r\nCódigo Error = {trxData.HostRespCode} {errMsg.Trim()}");
                    LOG.Error("{Message}", $"Error trying to purchase with CC[{ccCode}].\r\n Error Code = ({trxData.HostRespCode}) {errMsg.Trim()}.\r\n{trxData.HostMessage}");
                    return retVal;
                }

                retVal = (posResp == VPI_ERROR_CODE.VPI_OK);

                LOG.Info("{Message}", $"LAPOS Purchase operation returned from host:\r\n{trxData.DumpString()}");

                if (ConfigMgr.Instance.LAPOSShowPaymentResultDialog)
                    OpsContext.ShowMessage(trxData.DumpString());

                //Send Tender media
                if (!fromTender)
                {
                    AddTender(tndrObjNum_, amount_, trxData.AuthCode);
                    mTenderTriggeredByInterface = true;
                }
            }
            catch (Exception ex)
            {
                LOG.Fatal(ex, "{Message}", "Exception caught.");
            }
            finally
            {
                LOG.Debug("EXIT");
            }

            return retVal;
        }

        private VPI_ERROR_CODE TestLAPOSDevice(ref string errMsg)
        {
            VPI_ERROR_CODE posResp;
            //Check POS Device
            int retries = 0;
            do
            {
                posResp = IntegratedPOS.TestConnection(ref errMsg);

                if (posResp != VPI_ERROR_CODE.VPI_OK)
                {
                    Thread.Sleep(ConfigMgr.Instance.LAPOSWaitMs / 2);
                    if (retries++ > 10)
                        OpsContext.ShowError("Verifique que no haya quedado una tarjeta insertada. Si el error persiste, reinicie el POS");
                }
            } while (posResp != VPI_ERROR_CODE.VPI_OK);
            return posResp;
        }
        #endregion LAPOS Helper Methods

        #region Ops Commands
        private void AddTender(long tnObjNum_, decimal? amount_ = null, string reference = null)
        {
            //LAB ARG {es-AR}: para los medios de pago se utiliza la de sistema (",")
            NumberFormatInfo nfi = System.Globalization.CultureInfo.CurrentCulture.NumberFormat;

            if(ConfigMgr.Instance.UseSimphonyDecimalSeparatorForTenders)
            {
                nfi = new NumberFormatInfo()
                {
                    CurrencyDecimalSeparator = OpsContext.CurrentDecimalSeparator,
                    CurrencyGroupSeparator = "",
                    CurrencyGroupSizes = new int[] { 0 },
                    NumberDecimalSeparator = OpsContext.CurrentDecimalSeparator,
                    NumberGroupSeparator = "",
                    NumberGroupSizes = new int[] { 0 },
                };
            }

            List<OpsCommand> addTenderMacro = new List<OpsCommand>();

            if (amount_ != null)
                addTenderMacro.Add(new OpsCommand(OpsCommandType.Payment) { Number = tnObjNum_, Arguments = "Cash:Cash $ " + ((decimal)amount_).ToString("N", nfi) });
            else
                addTenderMacro.Add(new OpsCommand(OpsCommandType.Payment) { Number = tnObjNum_, Arguments = "Cash:Cash * " });

            if (reference != null)
            {
                addTenderMacro.Add(new OpsCommand(OpsCommandType.AsciiData) { Text = reference });
                addTenderMacro.Add(new OpsCommand(OpsCommandType.EnterKey));
            }

            OpsCommand opsCmd = new OpsCommand(OpsCommandType.Macro) { Data = addTenderMacro };

            OpsCommandMgr.SendOpsCommand(OpsContext, opsCmd, () => { return OpsContext.CheckIsOpen; });

            Thread.Sleep(100);
        }

        private void TransactionCancel()
        {
            List<OpsCommand> transactionCancelMacro = new List<OpsCommand>()
            {
                new OpsCommand(OpsCommandType.TransactionCancel) { },
                new OpsCommand(OpsCommandType.EnterKey),
            };

            OpsCommand opsCmd = new OpsCommand(OpsCommandType.Macro) { Data = transactionCancelMacro };

            OpsCommandMgr.SendOpsCommand(OpsContext, opsCmd, () => { return true; });
        }
        #endregion Ops Commands
        
        #region Ops Event Handlers
        private EventProcessingInstruction OnOpsInitEvent(object sender, OpsInitEventArgs args)
        {
            return EventProcessingInstruction.Continue;
        }

        //Handle this event to execute before any other tender event in the FI module 
        private EventProcessingInstruction OnOpsTmedPreviewEvent(object sender, OpsTmedEventArgs args)
        {
            EventProcessingInstruction retVal = EventProcessingInstruction.Continue;

            if (mTenderTriggeredByInterface)
            {
                mTenderTriggeredByInterface = false;
                return EventProcessingInstruction.Continue;
            }

            //this is a Credit card tender media
            if (args.TmedDef.OptionBits.CheckBit(43)) //check Tender Media Option Bit 43 - Do Not Print CC Voucher (this should be ON in ever Credit/Debit card Tender Media)
                if (!BeginPayment(args.Total, args.TmedDef.ObjectNumber))
                    retVal = EventProcessingInstruction.AbortEvent;

            return retVal;
        }

        private EventProcessingInstruction OnOpsTransactionCancelEvent(object sender, OpsTransactionCancelPreviewEventArgs args)
        {
            EventProcessingInstruction retVal = EventProcessingInstruction.Continue;

            mTenderTriggeredByInterface = false;

            return retVal;
        }

        private EventProcessingInstruction OnOpsErrorMessageEvent(object sender, OpsErrorMessageEventArgs args)
        {
            mTenderTriggeredByInterface = false;
            return EventProcessingInstruction.Continue;
        }
        #endregion OPS Event Handlers

        #region Helper Methods
        private List<PLAN_OUT> DoCCGetPlan()
        {
            List<PLAN_OUT> retVal = new List<PLAN_OUT>();
            VPI_ERROR_CODE retCode;
            string errMsg = "";

            if ((retCode = IntegratedPOS.GetPlan(out retVal, ref errMsg)) != VPI_ERROR_CODE.VPI_OK)
            {
                OpsContext.ShowError($"Error al intentar obtener el Listado de Tarjetas\r\n{errMsg}");
                LOG.Error("{Message}", $"LAPOS Get Issuer operation failed:\r\n{errMsg}");
                return retVal;
            }

            OpsContext.ShowTextList("Listado de Planes", retVal.Select(plan => plan.DumpString()));

            return retVal;
        }

        private List<ISSUER_OUT> DoCCGetCards(bool showCards_ = true)
        {
            List<ISSUER_OUT> retVal = new List<ISSUER_OUT>();
            VPI_ERROR_CODE retCode;
            string errMsg = "";

            if ((retCode = IntegratedPOS.GetIssuer(out retVal, ref errMsg)) != VPI_ERROR_CODE.VPI_OK)
            {
                OpsContext.ShowError($"Error al intentar obtener el Listado de Tarjetas\r\n{errMsg}");
                LOG.Error("{Message}", $"LAPOS Get Issuer operation failed:\r\n{errMsg}");

                return retVal;
            }

            if (showCards_)
                OpsContext.ShowTextList("Listado de Tarjetas", retVal.Select(card => card.DumpString()));

            return retVal;
        }

        private string GetCardPlan(string laposCardCode_, int instalment)
        {
            string retVal = " ";

            try
            {
                retVal = (mCardPlanMap.ContainsKey(laposCardCode_) ? mCardPlanMap[laposCardCode_].ContainsKey(instalment) ? mCardPlanMap[laposCardCode_][instalment] : " " : " ");
            }
            catch (Exception ex)
            {
                LOG.Fatal(ex, "{Message}", "Exception caught.");
            }

            return retVal;
        }

        private Dictionary<string, Dictionary<int, string>> GetCCPlanDef()
        {
            Dictionary<string, Dictionary<int, string>> retVal = new Dictionary<string, Dictionary<int, string>>();

            string cardMapString = ConfigMgr.Instance.LAPOSCreditCardPlanDef;

            string[] cardDefStrList = cardMapString.Split(';');

            foreach (string cardDefStr in cardDefStrList)
            {
                try
                {
                    string[] cardDef = cardDefStr.Split(':');

                    if (cardDef.Length == 3)
                    {
                        if (!retVal.ContainsKey(cardDef[0].Trim()))
                            retVal.Add(cardDef[0].Trim(), new Dictionary<int, string>());

                        retVal[cardDef[0].Trim()].Add(int.Parse(cardDef[1]), cardDef[2]);
                    }
                }
                catch (Exception ex)
                {
                    LOG.Fatal(ex, "{Message}", "Exception caught.");
                }
            }

            return retVal;
        }

        private string SelectCardFromList()
        {
            string retVal = "";

            try
            {
                if (mCardDefList.Count == 0)
                    return retVal;

                List<OpsSelectionEntry> entryList = new List<OpsSelectionEntry>();
                entryList.Add(new OpsSelectionEntry(0, "-- Detectar Tarjeta --", 0));
                for (int index = 1; index <= mCardDefList.Count; index++)
                    entryList.Add(new OpsSelectionEntry(index , mCardDefList[index].IssuerName, index));

                int selectedOption = OpsContext.SelectionRequest("Tarjetas", "Seleccione Tarjeta", entryList) ?? -1;

                if (selectedOption > 0 )
                    retVal = mCardDefList[selectedOption].IssuerCode.TrimStart('0');
            }
            catch (Exception ex)
            { 
            }

            return retVal;
        }
        #endregion Helper Methods
    }
}
