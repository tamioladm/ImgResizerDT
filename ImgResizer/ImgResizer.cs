using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ImgResizer
{
    public static class ImgResizer
    {
        const string StorageAccountConnectionString = "DefaultEndpointsProtocol=https;AccountName=imgresizerstorageacc;AccountKey=iS6UgPy5PFPLLsuQMuviRgoRFhOtZjZkrAVfnljeZYR2vc5HggLXJysyl+TOjNvA+pi48tF1a2WATzKfU60VBA==;EndpointSuffix=core.windows.net";

        [FunctionName("ImgResizerHTTPTrigger")]
        public static async Task<IActionResult> GetResizedImage(
                [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "images-fullres/{filename}")] HttpRequest req,
                [Blob("images-fullres/{filename}", FileAccess.Read, Connection = "AzureWebJobsStorage")] Stream inputStream,
                [Blob("images-misc/watermark.png", FileAccess.Read, Connection = "AzureWebJobsStorage")] Stream watermarkStream,
                string filename,
                ILogger log)
        {

            // Wywala 500 Internal Server Error zanim w ogóle to sprawdzi.
            //if (req.Query["w"] != int.Parse(req.Query["w"]).ToString() || req.Query["h"] != int.Parse(req.Query["h"]).ToString() || int.Parse(req.Query["w"]) <= 0 || int.Parse(req.Query["h"]) <= 0)
            //{
            //    return new BadRequestObjectResult("Invalid non-optional parameters.");
            //}

            // Pobieramy parametry z GETa do zmiennych.
            string width = req.Query["w"]; // Szerokość obrazka.
            string height = req.Query["h"]; // Wysokość obrazka.
            string center = req.Query["c"]; // Czy obrazek ma być wycentrowany, przyjmuje 'yes'.
            string watermark = req.Query["wm"]; // Czy obrazek ma mieć watermark, przyjmuje 'logo'.

            int imgWidth = (int.Parse(width) > 5000 ? 5000 : int.Parse(width));
            int imgHeight = (int.Parse(height) > 5000 ? 5000 : int.Parse(height));


            // To zwraca IActionResult w przypadku OkObjectResult.
            string returnMessage = "";

            // Łączenie się ze Azure Storage Account i tworzenie klienta.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(StorageAccountConnectionString);
            CloudBlobClient client = storageAccount.CreateCloudBlobClient();

            // Pobieranie bloba wejściowego tylko w celu sprawdzenia, czy istnieje. Image.Load(inputStream) nie działa.
            CloudBlobContainer inputContainer = client.GetContainerReference("images-fullres");
            CloudBlobDirectory inputDirectory = inputContainer.GetDirectoryReference("");
            CloudBlockBlob inputBlob = inputDirectory.GetBlockBlobReference($"{filename}");
            //Stream inputStream = new MemoryStream();
            //await inputBlob.DownloadToStreamAsync(inputStream);

            // Alternatywne pobieranie watermarka. Image.Load(watermarkStream) wywala to samo, co w przypadku inputStream.
            //CloudBlobContainer miscContainer = client.GetContainerReference("images-misc");
            //CloudBlobDirectory miscDirectory = miscContainer.GetDirectoryReference("");
            //CloudBlockBlob watermarkBlob = miscDirectory.GetBlockBlobReference("watermark.png");
            //Stream watermarkStream = new MemoryStream();
            //await watermarkBlob.DownloadToStreamAsync(watermarkStream);

            // Pobieranie bloba wyjściowego, do niego zapisujemy gotowy obrazek.
            CloudBlobContainer outputContainer = client.GetContainerReference("images-thumb");
            outputContainer.CreateIfNotExists();
            CloudBlobDirectory outputDirectory = outputContainer.GetDirectoryReference($"{imgWidth}/{imgHeight}");
            if (center == "yes") { outputDirectory = outputContainer.GetDirectoryReference($"{imgWidth}/{imgHeight}/center"); }
            CloudBlockBlob outputBlob = outputDirectory.GetBlockBlobReference($"thumb-{filename}");
            if (watermark == "logo") { outputBlob = outputDirectory.GetBlockBlobReference($"watermark-thumb-{filename}"); }
            Stream outputStream = new MemoryStream();

            // Jeżeli obrazek do przeskalowania nie istnieje.
            if (!inputBlob.Exists())
            {
                return new BadRequestObjectResult("Result:\n" +
                "The image doesn't exist.");
            }
            // Jeżeli obrazek o podanych wymiarach już istnieje.
            else if (outputBlob.Exists())
            {
                return new OkObjectResult($"Result:\n" +
                $"The image already exists in the given dimensions.\n" +
                $"Image link:\n" +
                $"{outputBlob.Uri}");
            }
            // Obrazek do przeskalowania istnieje, ale nie istnieje jego cache.
            else
            {
                using (var image = Image.Load(inputStream))
                {
                    var imageResult = image;
                    // Jeżeli ma być centrowany.
                    if (center == "yes")
                    {
                        try
                        {
                            imageResult.Mutate(
                                x => x.Resize(new ResizeOptions
                                    {
                                        Mode = ResizeMode.Max,
                                        Size = new Size(imgWidth, imgHeight)
                                    }
                                )
                            );

                            var imageContainer = new Image<Rgba32>(imgWidth, imgHeight, Color.FromRgb(0, 0, 0));
                            // Jeżeli brakuje mu pikseli w poziomie.
                            if (imageResult.Width < imageContainer.Width)
                            {
                                imageContainer.Mutate(x => x.DrawImage(imageResult, new Point((imageContainer.Width / 2) - (imageResult.Width / 2), 0), 1.0f));
                            }
                            // Jeżeli brakuje mu pikseli w pionie.
                            if (imageResult.Height < imageContainer.Height)
                            {
                                imageContainer.Mutate(x => x.DrawImage(imageResult, new Point(0, (imageContainer.Height / 2) - (imageResult.Height / 2)), 1.0f));
                            }

                            imageResult = imageContainer;

                            returnMessage = $"Image resized and centered successfully. Dimensions: {imgWidth}x{imgHeight}";
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("{0} Exception caught while centering or resizing image.", e);
                        }
                    }
                    // Jeżeli nie ma być centrowany.
                    else
                    {
                        try
                        {
                            imageResult.Mutate(
                                i => i.Resize(imgWidth, imgHeight)
                            );
                            returnMessage = $"Image resized successfully. Dimensions: {imgWidth}x{imgHeight}";
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("{0} Exception caught while resizing image.", e);
                        }
                        
                    }
                    // Jeżeli ma mieć watermark.
                    if (watermark == "logo")
                    {
                        using (var watermarkImage = Image.Load(watermarkStream))
                        {
                            try
                            {
                                watermarkImage.Mutate(
                                    x => x.Resize(new ResizeOptions
                                        {
                                            Mode = ResizeMode.Max,
                                            Size = new Size(imgWidth / 3, imgHeight)
                                        }
                                    )
                                );
                                imageResult.Mutate(x => x.DrawImage(watermarkImage, new Point(10, 10), 0.5f));
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("{0} Exception caught while applying watermark.", e);
                            }
                        }
                    }
                    // Zapisujemy wynik do streama, stream do bloba.
                    try
                    {
                        imageResult.Save(outputStream, new JpegEncoder());
                        outputStream.Seek(0, SeekOrigin.Begin);
                        await outputBlob.UploadFromStreamAsync(outputStream);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("{0} Exception caught while saving image.", e);
                    }
                }
            }
            return new OkObjectResult($"Result:\n" +
                $"{returnMessage}\n" +
                $"Image link:\n" +
                $"{outputBlob.Uri}");
        }


        [FunctionName("ImgUploaderHTTPTrigger")]
        public static async Task<IActionResult> PutUploadImage(
                [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "upload")] HttpRequest req,
                ILogger log)
        {
            IFormFile inputStream = req.Form.Files.GetFile(req.Form.Files[0].Name);
            string putFilename = req.Form.Files[0].FileName;

            string returnMessage = "";

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(StorageAccountConnectionString);
            CloudBlobClient client = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference("images-fullres");
            container.CreateIfNotExists();
            container.SetPermissions(new BlobContainerPermissions { PublicAccess = BlobContainerPublicAccessType.Container });
            CloudBlobDirectory outputDirectory = container.GetDirectoryReference("");
            CloudBlockBlob outputBlob = outputDirectory.GetBlockBlobReference($"{putFilename}");
            Stream outputStream = new MemoryStream();

            if (outputBlob.Exists())
            {
                return new BadRequestObjectResult("An image with this name already exists. Sorry, we currently do not support overwriting.");
            }
            else
            {
                returnMessage = "Image uploaded successfully.";
                using (var image = Image.Load(inputStream.OpenReadStream()))
                {
                    try
                    {
                        image.Save(outputStream, new JpegEncoder());
                        outputStream.Seek(0, SeekOrigin.Begin);
                        await outputBlob.UploadFromStreamAsync(outputStream);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("{0} Exception caught while uploading image.", e);
                    }
                    
                }
            }

            return new OkObjectResult($"Result:\n" +
                $"{returnMessage}\n" +
                $"Image link:\n" +
                $"{outputBlob.Uri}");
        }

        [FunctionName("ImgDeleteHTTPTrigger")]
        public static async Task<IActionResult> DeleteImage(
                [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "delete/{filename}")] HttpRequest req,
                string filename,
                ILogger log)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(StorageAccountConnectionString);
            CloudBlobClient client = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference("images-fullres");
            CloudBlobContainer containerThumb = client.GetContainerReference("images-thumb");
            CloudBlobDirectory directory = container.GetDirectoryReference("");
            CloudBlockBlob inputBlob = directory.GetBlockBlobReference($"{filename}");

            // Boję się usunąć jakikolwiek argument stąd, skopiowane z internetu działa bez zarzutu więc zostaje.
            BlobResultSegment resultSegment = await containerThumb.ListBlobsSegmentedAsync("", true, BlobListingDetails.All, 10, null, null, null);
            var listCloudBlob = resultSegment.Results.Select(x => x as ICloudBlob);

            if (!inputBlob.Exists())
            {
                return new BadRequestObjectResult($"File {filename} didn't exist in the first place.");
            }
            else
            {
                try
                {
                    foreach (ICloudBlob cloudBlob in listCloudBlob)
                    {

                        if (cloudBlob.Uri.ToString().EndsWith($"thumb-{filename}"))
                        {
                            Console.WriteLine("Deleted: " + cloudBlob.Uri);
                            await cloudBlob.DeleteIfExistsAsync();
                        }

                    }
                    Console.WriteLine("Deleted: " + inputBlob.Uri);
                    await inputBlob.DeleteIfExistsAsync();
                }
                catch (Exception e)
                {
                    Console.WriteLine("{0} Exception caught while deleting blobs.", e);
                }


            }


            return new OkObjectResult($"File {filename} deleted successfully.");
        }
    }
}