using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScheduleAppApi.Models;
using ScheduleAppApi.Services;
using System.Security.Claims;
using System.Diagnostics;

namespace ScheduleAppApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SchedulesController : ControllerBase
    {
        private readonly IScheduleService _scheduleService;

        public SchedulesController(IScheduleService scheduleService)
        {
            _scheduleService = scheduleService;
        }

        private int? GetUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
            {
                return userId;
            }
            return null;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateScheduleDto dto)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized(new { message = "Token không hợp lệ hoặc không tìm thấy User ID." });

            if (dto.StartTime >= dto.EndTime)
            {
                return BadRequest(new { message = "Thời gian kết thúc phải sau thời gian bắt đầu." });
            }

            try
            {
                var result = await _scheduleService.CreateScheduleAsync(dto, userId.Value);

                return Ok(new { message = "Tạo lịch thành công.", result = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error creating schedule: " + ex.Message);
                return StatusCode(500, new { message = "Lỗi server khi tạo lịch: " + ex.Message });
            }
        }

        [HttpGet("mine")]
        public async Task<IActionResult> GetMySchedules()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            try
            {
                var schedules = await _scheduleService.GetMySchedulesAsync(userId.Value);
                return Ok(schedules);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error getting schedules: " + ex.Message);
                return StatusCode(500, new { message = "Lỗi server khi lấy danh sách lịch." });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateScheduleDto dto)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            if (dto.StartTime >= dto.EndTime)
            {
                return BadRequest(new { message = "Thời gian kết thúc phải sau thời gian bắt đầu." });
            }

            try
            {
                var success = await _scheduleService.UpdateScheduleAsync(id, dto, userId.Value);

                if (!success)
                {
                    return NotFound(new { message = "Không tìm thấy lịch hoặc bạn không có quyền chỉnh sửa." });
                }

                return Ok(new { message = "Cập nhật lịch thành công." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            try
            {
                var success = await _scheduleService.DeleteScheduleAsync(id, userId.Value);

                if (!success)
                {
                    return NotFound(new { message = "Không tìm thấy lịch hoặc bạn không có quyền xóa." });
                }

                return Ok(new { message = "Xóa lịch thành công." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            return StatusCode(501, new { message = "Chức năng này chưa được hỗ trợ." });
        }
    }
}