using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO.Ports;
using TWS.Configurator;

namespace bp.simphony.payment
{
    public class ConfigMgr : ConfiguratorBase
    {
        #region Singleton
        private static ConfigMgr mConfiguration = new ConfigMgr();

        static ConfigMgr()
        { }

        private ConfigMgr()
        {
        }

        public static ConfigMgr Instance
        {
            get { return mConfiguration; }
        }
        #endregion Singleton

        public void Initialize(string fileName_)
        {
            base.mFileName = fileName_;
        }

        #region General

        public bool ShowIFCVersion
        {
            get { return GetBoolenValue("SHOW_VERSION"); }
        }

        #endregion general

        public Dictionary<string, int> PrismaCCToSimphonyObjNumMap
        {
            get => GetDictionary<string, int>("LAPOS_TO_SIMPHONY_CARD_MAP");
        }

        public bool UseSimphonyDecimalSeparatorForTenders
        {
            get => GetBoolenValue("USE_SIMPHONY_DECIMAL_SEPARATOR_FOR_TENDERS");
        }

        public string LAPOSMerchantCUIT
        {
            get => GetStringValue("LAPOS_MERCHANT_CUIT").Trim();
        }

        public string LAPOSMerchantCode
        {
            get => GetStringValue("LAPOS_MERCHANT_CODE").Trim();
        }

        public string LAPOSMerchantName
        {
            get => GetStringValue("LAPOS_MERCHANT_NAME").Trim();
        }
        
        public string LAPOSCreditCardPlanDef
        {
            get => GetStringValue("LAPOS_CREDIT_CARD_PLAN_DEF");
        }

        public bool LAPOSShowPaymentResultDialog
        {
            get => GetBoolenValue("LAPOS_SHOW_PAYMENT_RESULT_DIALOG");
        }

        public string LAPOSSerialPortNumber
        {
            get => GetStringValue("LAPOS_SERIAL_PORT_NUMBER"); 
        }

        public string LAPOSSerialPortBaudRate
        {
            get => GetStringValue("LAPOS_SERIAL_PORT_BAUDRATE"); 
        }

        public string LAPOSSerialPortByteSize
        {
            get => GetStringValue("LAPOS_SERIAL_PORT_BYTESIZE"); 
        }

        public string LAPOSSerialPortParity
        {
            get => GetStringValue("LAPOS_SERIAL_PORT_PARITY"); 
        }

        public string LAPOSSerialPortStopBits
        {
            get => GetStringValue("LAPOS_SERIAL_PORT_STOPBITS"); 
        }

        public int LAPOSWaitMs
        {
            get => GetIntegerValue("LAPOS_WAIT_MS");
        }
        
        /*
        public string MercadoLibreAPIURL
        {
            get { return GetStringValue("MERCADO_LIBRE_API_URL").Trim(); }
        }

        public string MercadoPagoAPIURL
        {
            get { return GetStringValue("MERCADO_PAGO_API_URL").Trim(); }
        }

        public string MercadoLibreAPIAccessTOKEN
        {
            get { return GetStringValue("MERCADO_PAGO_API_ACCESS_TOKEN").Trim(); }
        }

        public string QRPostOrderResource
        {
            get { return GetStringValue("QR_POST_ORDER_RESOURCE").Trim(); }
        }

        public string PayerQRPostOrderResource
        {
            get { return GetStringValue("PAYER_QR_POST_ORDER_RESOURCE").Trim(); }
        }

        public string PaymentSearchResource
        {
            get { return GetStringValue("PAYMENT_SEARCH_RESOURCE").Trim(); }
        }

        public string CustomerName
        {
            get { return GetStringValue("CUSTOMER_NAME").Trim(); }
        }

        public string CustomerFiscalID
        {
            get { return GetStringValue("CUSTOMER_FISCAL_ID").Trim(); }
        }

        public string CollectorID
        {
            get { return GetStringValue("COLLECTOR_ID").Trim(); }
        }

        public string StoreID
        {
            get { return GetStringValue("STORE_ID").Trim(); }
        }

        public string Currency
        {
            get { return GetStringValue("MP_CURRENCY").Trim(); }
        }

        public int MP_TenderObjNumber
        {
            get { return GetIntegerValue("MP_TENDER_OBJ_NUM"); }
        }

        public int Timeout
        {
            get { return GetIntegerValue("MP_TIMEOUT"); }
        }

        public string SponsorID
        {
            get { return GetStringValue("SPONSOR_ID").Trim(); }
        }

        public string NotificationURL
        {
            get { return GetStringValue("NOTIFICATION_URL").Trim(); }
        }
        */
    }
}
