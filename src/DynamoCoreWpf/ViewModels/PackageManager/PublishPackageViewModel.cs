using Dynamo.ViewModels;
using NotificationObject = Microsoft.Practices.Prism.ViewModel.NotificationObject;
using Dynamo.Wpf.Utilities;

namespace Dynamo.PackageManager
{
    internal delegate void PublishSuccessHandler(PublishCefHelper sender);

    /// <summary>
    /// The ViewModel for Package publishing </summary>
    public class PublishPackageViewModel : NotificationObject
    {
        internal PublishCefHelper CefHelper { get; set; }
        /// <summary>
        /// The class constructor. </summary>
        public PublishPackageViewModel(DynamoViewModel dynamoViewModel)
        {
            CefHelper = new PublishCefHelper(dynamoViewModel, null, null);
        }

    }

}
