﻿
namespace AiTool3
{
    internal class ImageHelpers
    {
        internal static string GetImageType(string fileName)
        {
            // return the mime type for png, jpg, jpeg, bmp
            var fileExt = Path.GetExtension(fileName).ToLower();
            switch (fileExt)
            {
                case ".png":
                    return "image/png";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".bmp":
                    return "image/bmp";
                default:
                    return "image/png";
            }
        }

        internal static string ImageToBase64(string fileName)
        {
            //open the image file and return it as a base64 string
            using (var image = Image.FromFile(fileName))
            {
                using (var ms = new MemoryStream())
                {
                    image.Save(ms, image.RawFormat);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }
    }
}