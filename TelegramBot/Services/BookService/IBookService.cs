using TelegramBot.Domain.DTOs;

namespace TelegramBot.Services.BookService;

public interface IBookService
{
    Task<GetBookDTO> CreateBookAsync(CreateBookDTO createBookDto, string filePath);
    Task<GetBookDTO> GetBookByIdAsync(int bookId);
    Task<List<GetBookDTO>> GetBooksByCategoryAsync(int categoryId);
    Task<List<GetBookDTO>> GetAllBooksAsync();
    Task<bool> DeleteBookAsync(int bookId);
    Task<bool> UpdateBookAsync(int bookId, CreateBookDTO updateBookDto);
    Task<string> GetBookFilePathAsync(int bookId);
    Task IncrementDownloadCountAsync(int bookId);
    Task<GetBookCategoryDTO> CreateCategoryAsync(CreateBookCategoryDTO createCategoryDto);
    Task<List<GetBookCategoryDTO>> GetAllCategoriesAsync();
    Task<GetBookCategoryDTO> GetCategoryByIdAsync(int categoryId);
    Task<bool> DeleteCategoryAsync(int categoryId);
    Task<List<string>> GetClustersAsync();
    Task<List<int>> GetYearsAsync();
} 