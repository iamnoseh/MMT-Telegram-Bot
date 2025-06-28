using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TelegramBot.Domain.DTOs;
using TelegramBot.Domain.Entities;

namespace TelegramBot.Services.BookService;

public class BookService : IBookService
{
    private readonly DataContext _context;
    private readonly string _filesDirectory;

    public BookService(DataContext context, IConfiguration configuration)
    {
        _context = context;
        _filesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Files", "Books");
        
        // Ensure directory exists
        if (!Directory.Exists(_filesDirectory))
        {
            Directory.CreateDirectory(_filesDirectory);
        }
    }

    public async Task<GetBookDTO> CreateBookAsync(CreateBookDTO createBookDto, string filePath)
    {
        var book = new Book
        {
            Title = createBookDto.Title,
            Description = createBookDto.Description,
            CategoryId = createBookDto.CategoryId,
            FileName = createBookDto.FileName,
            FilePath = filePath,
            FileExtension = createBookDto.FileExtension,
            FileSize = createBookDto.FileSize,
            UploadedByUserId = createBookDto.UploadedByUserId,
            UploadedAt = DateTime.UtcNow,
            IsActive = true,
            DownloadCount = 0
        };

        _context.Books.Add(book);
        await _context.SaveChangesAsync();

        return await GetBookByIdAsync(book.Id);
    }

    public async Task<GetBookDTO> GetBookByIdAsync(int bookId)
    {
        var book = await _context.Books
            .Include(b => b.Category)
            .Include(b => b.UploadedByUser)
            .FirstOrDefaultAsync(b => b.Id == bookId && b.IsActive);

        if (book == null)
            return null;

        return new GetBookDTO
        {
            Id = book.Id,
            Title = book.Title,
            Description = book.Description,
            FileName = book.FileName,
            FileExtension = book.FileExtension,
            FileSize = book.FileSize,
            UploadedAt = book.UploadedAt,
            DownloadCount = book.DownloadCount,
            CategoryName = book.Category?.Name,
            Cluster = book.Category?.Cluster,
            Year = book.Category?.Year ?? 0,
            UploadedByUserName = book.UploadedByUser?.Name
        };
    }

    public async Task<List<GetBookDTO>> GetBooksByCategoryAsync(int categoryId)
    {
        var books = await _context.Books
            .Include(b => b.Category)
            .Include(b => b.UploadedByUser)
            .Where(b => b.CategoryId == categoryId && b.IsActive)
            .OrderByDescending(b => b.UploadedAt)
            .ToListAsync();

        return books.Select(b => new GetBookDTO
        {
            Id = b.Id,
            Title = b.Title,
            Description = b.Description,
            FileName = b.FileName,
            FileExtension = b.FileExtension,
            FileSize = b.FileSize,
            UploadedAt = b.UploadedAt,
            DownloadCount = b.DownloadCount,
            CategoryName = b.Category?.Name,
            Cluster = b.Category?.Cluster,
            Year = b.Category?.Year ?? 0,
            UploadedByUserName = b.UploadedByUser?.Name
        }).ToList();
    }

    public async Task<List<GetBookDTO>> GetAllBooksAsync()
    {
        var books = await _context.Books
            .Include(b => b.Category)
            .Include(b => b.UploadedByUser)
            .Where(b => b.IsActive)
            .OrderByDescending(b => b.UploadedAt)
            .ToListAsync();

        return books.Select(b => new GetBookDTO
        {
            Id = b.Id,
            Title = b.Title,
            Description = b.Description,
            FileName = b.FileName,
            FileExtension = b.FileExtension,
            FileSize = b.FileSize,
            UploadedAt = b.UploadedAt,
            DownloadCount = b.DownloadCount,
            CategoryName = b.Category?.Name,
            Cluster = b.Category?.Cluster,
            Year = b.Category?.Year ?? 0,
            UploadedByUserName = b.UploadedByUser?.Name
        }).ToList();
    }

    public async Task<bool> DeleteBookAsync(int bookId)
    {
        var book = await _context.Books.FindAsync(bookId);
        if (book == null)
            return false;

        // Delete physical file
        if (File.Exists(book.FilePath))
        {
            File.Delete(book.FilePath);
        }

        book.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateBookAsync(int bookId, CreateBookDTO updateBookDto)
    {
        var book = await _context.Books.FindAsync(bookId);
        if (book == null)
            return false;

        book.Title = updateBookDto.Title;
        book.Description = updateBookDto.Description;
        book.CategoryId = updateBookDto.CategoryId;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<string> GetBookFilePathAsync(int bookId)
    {
        var book = await _context.Books.FindAsync(bookId);
        return book?.FilePath;
    }

    public async Task IncrementDownloadCountAsync(int bookId)
    {
        var book = await _context.Books.FindAsync(bookId);
        if (book != null)
        {
            book.DownloadCount++;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<GetBookCategoryDTO> CreateCategoryAsync(CreateBookCategoryDTO createCategoryDto)
    {
        var category = new BookCategory
        {
            Name = createCategoryDto.Name,
            Cluster = createCategoryDto.Cluster,
            Year = createCategoryDto.Year,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.BookCategories.Add(category);
        await _context.SaveChangesAsync();

        return await GetCategoryByIdAsync(category.Id);
    }

    public async Task<List<GetBookCategoryDTO>> GetAllCategoriesAsync()
    {
        var categories = await _context.BookCategories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Cluster)
            .ThenBy(c => c.Year)
            .ToListAsync();

        var result = new List<GetBookCategoryDTO>();
        foreach (var category in categories)
        {
            var bookCount = await _context.Books.CountAsync(b => b.CategoryId == category.Id && b.IsActive);
            result.Add(new GetBookCategoryDTO
            {
                Id = category.Id,
                Name = category.Name,
                Cluster = category.Cluster,
                Year = category.Year,
                BookCount = bookCount
            });
        }

        return result;
    }

    public async Task<GetBookCategoryDTO> GetCategoryByIdAsync(int categoryId)
    {
        var category = await _context.BookCategories.FindAsync(categoryId);
        if (category == null || !category.IsActive)
            return null;

        var bookCount = await _context.Books.CountAsync(b => b.CategoryId == categoryId && b.IsActive);

        return new GetBookCategoryDTO
        {
            Id = category.Id,
            Name = category.Name,
            Cluster = category.Cluster,
            Year = category.Year,
            BookCount = bookCount
        };
    }

    public async Task<bool> DeleteCategoryAsync(int categoryId)
    {
        var category = await _context.BookCategories.FindAsync(categoryId);
        if (category == null)
            return false;

        category.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<string>> GetClustersAsync()
    {
        return await _context.BookCategories
            .Where(c => c.IsActive)
            .Select(c => c.Cluster)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }

    public async Task<List<int>> GetYearsAsync()
    {
        return await _context.BookCategories
            .Where(c => c.IsActive)
            .Select(c => c.Year)
            .Distinct()
            .OrderByDescending(c => c)
            .ToListAsync();
    }
} 