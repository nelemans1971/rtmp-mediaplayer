// For android to compile this library The Assembly name and Default Namespace must not contain a .
// This is the reason they are set to "LIBRTMPNET" for android
//
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if __ANDROID__
// Android-specific code
using Android.App;
#endif

// General Information about an assembly is controlled through the following 
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("LibRTMP.NET")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Muziekweb")]
[assembly: AssemblyProduct("LibRTMP.NET")]
[assembly: AssemblyCopyright("Copyright © Muziekweb 2013")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible 
// to COM components.  If you need to access a type in this assembly from 
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("3c21f6eb-08ca-43a7-baa5-775cc7759657")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version 
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers 
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("1.00.0.0")]
[assembly: AssemblyFileVersion("1.00.0.0")]

#if __ANDROID__
// Android-specific code
// Add some common permissions, these can be removed if not needed
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]
[assembly: UsesPermission(Android.Manifest.Permission.WriteExternalStorage)]
#endif