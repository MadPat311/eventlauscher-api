using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using EventLauscherApi.Data;
using EventLauscherApi.Models;
using Microsoft.AspNetCore.Authorization;

[Route("api/[controller]")]
[ApiController]
public class MediaFilesController : ControllerBase
{
    private readonly EventContext _context;
    public MediaFilesController(EventContext context) => _context = context;

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> UploadMediaFile([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Kein File empfangen.");

        // Datei in Memory lesen
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var data = ms.ToArray();

        // Hash berechnen (z.B. SHA256)
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(data);
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

        // Prüfe, ob Datei schon existiert (Hash-Vergleich)
        var exists = _context.MediaFiles.Any(m => m.Hash == hashString);
        if (exists)
            return Conflict("Datei existiert bereits (gleicher Hash).");

        var mediaFile = new MediaFile
        {
            FileName = file.FileName,
            Data = data,
            Hash = hashString
        };

        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync();

        return Ok(new { id = mediaFile.Id, hash = hashString });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetImage(int id)
    {
        var file = await _context.MediaFiles.FindAsync(id);
        if (file == null)
            return NotFound();

        return File(file.Data, "application/octet-stream", file.FileName);
        // Oder: image/jpeg, image/png... je nach Endung prüfen, falls du willst
    }
    [HttpGet]
    public IActionResult GetImages([FromQuery] int[] ids)
    {
        var files = _context.MediaFiles.Where(f => ids.Contains(f.Id)).ToList();

        // Optional: als Base64 übertragen (bei wenig Daten)
        var result = files.Select(f => new
        {
            id = f.Id,
            fileName = f.FileName,
            base64 = Convert.ToBase64String(f.Data)
        });

        return Ok(result);
    }

    [HttpGet("list")]
    public IActionResult ListFiles()
    {
        var files = _context.MediaFiles
            .Select(f => new { f.Id, f.FileName, f.UploadedAt })
            .ToList();
        return Ok(files);
    }


    [HttpGet("byhash/{hash}")]
    public IActionResult GetByHash(string hash)
    {
        var file = _context.MediaFiles.FirstOrDefault(f => f.Hash == hash);
        if (file == null)
            return Ok(new { id = -1 }); // Oder: new { id = (int?)null }

        return Ok(new { id = file.Id });
    }

}
