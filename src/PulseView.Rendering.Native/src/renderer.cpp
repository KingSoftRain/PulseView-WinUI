#include "pulseview/rendering/renderer.h"

#include "pulseview/core/viewport.h"

#include <algorithm>
#include <cmath>
#include <cwchar>
#include <cstdint>
#include <new>
#include <string>
#include <string_view>
#include <vector>

#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <d2d1_1.h>
#include <d3d11.h>
#include <dxgi1_3.h>
#include <microsoft.ui.xaml.media.dxinterop.h>
#include <wrl/client.h>

using Microsoft::WRL::ComPtr;

namespace {

constexpr int pv_rendering_ok = 0;
constexpr int pv_rendering_invalid_argument = -1;
constexpr int pv_rendering_initialization_failed = -2;
constexpr int pv_rendering_render_failed = -3;
constexpr int pv_rendering_unexpected_error = -100;

thread_local std::wstring last_error;

struct AnalogSegment {
    float x0;
    float y0;
    float x1;
    float y1;
    std::uint8_t channel_index;
    std::uint8_t flags;
};

struct DecoderAnnotation {
    float x0;
    float x1;
    std::uint8_t row_index;
    std::wstring text;
};

constexpr std::uint8_t dense_digital_span = 4;
constexpr std::uint8_t digital_sample_point = 8;
constexpr std::uint8_t analog_segment_envelope = 1;
constexpr std::uint8_t analog_segment_point = 2;
constexpr float waveform_left_margin = 54.0F;
constexpr float waveform_right_margin = 18.0F;

void set_last_error(std::wstring message)
{
    last_error = std::move(message);
}

int copy_wide_string(std::wstring_view value, wchar_t* buffer, int buffer_length)
{
    if (buffer == nullptr || buffer_length <= 0) {
        return static_cast<int>(value.size());
    }

    const auto capacity = static_cast<std::size_t>(buffer_length);
    const auto chars_to_copy = (std::min)(value.size(), capacity - 1);
    std::copy_n(value.data(), chars_to_copy, buffer);
    buffer[chars_to_copy] = L'\0';

    return static_cast<int>(chars_to_copy);
}

std::size_t bounded_wcslen(const wchar_t* value, std::size_t max_length)
{
    if (value == nullptr) {
        return 0;
    }

    std::size_t length = 0;
    while (length < max_length && value[length] != L'\0') {
        ++length;
    }

    return length;
}

std::wstring format_hresult(std::wstring_view operation, HRESULT hr)
{
    wchar_t buffer[160] = {};
    swprintf_s(buffer, L"%ls failed with HRESULT 0x%08X.", operation.data(), static_cast<unsigned int>(hr));
    return buffer;
}

std::wstring feature_level_name(D3D_FEATURE_LEVEL feature_level)
{
    switch (feature_level) {
    case D3D_FEATURE_LEVEL_11_1:
        return L"11.1";
    case D3D_FEATURE_LEVEL_11_0:
        return L"11.0";
    case D3D_FEATURE_LEVEL_10_1:
        return L"10.1";
    case D3D_FEATURE_LEVEL_10_0:
        return L"10.0";
    default:
        return L"unknown";
    }
}

class Renderer final {
public:
    int attach_swap_chain_panel(void* panel_unknown)
    {
        if (panel_unknown == nullptr) {
            set_last_error(L"SwapChainPanel pointer must not be null.");
            return pv_rendering_invalid_argument;
        }

        const HRESULT device_result = ensure_device();
        if (FAILED(device_result)) {
            set_last_error(format_hresult(L"Creating Direct2D/Direct3D device resources", device_result));
            return pv_rendering_initialization_failed;
        }

        ComPtr<IUnknown> unknown;
        unknown.Attach(static_cast<IUnknown*>(panel_unknown));
        unknown->AddRef();

        ComPtr<ISwapChainPanelNative> panel_native;
        HRESULT hr = unknown.As(&panel_native);
        if (FAILED(hr)) {
            set_last_error(format_hresult(L"Querying ISwapChainPanelNative", hr));
            return pv_rendering_initialization_failed;
        }

        panel_native_ = panel_native;
        hr = create_swap_chain();
        if (FAILED(hr)) {
            set_last_error(format_hresult(L"Creating composition swap chain", hr));
            return pv_rendering_initialization_failed;
        }

        hr = panel_native_->SetSwapChain(swap_chain_.Get());
        if (FAILED(hr)) {
            set_last_error(format_hresult(L"Attaching swap chain to SwapChainPanel", hr));
            return pv_rendering_initialization_failed;
        }

        return render_demo();
    }

    int resize(int pixel_width, int pixel_height, float dpi)
    {
        if (pixel_width <= 0 || pixel_height <= 0) {
            return pv_rendering_ok;
        }

        pixel_width_ = pixel_width;
        pixel_height_ = pixel_height;
        dpi_ = dpi > 0.0F ? dpi : 96.0F;

        if (!swap_chain_) {
            return pv_rendering_ok;
        }

        d2d_context_->SetTarget(nullptr);
        target_bitmap_.Reset();

        const HRESULT hr = swap_chain_->ResizeBuffers(
            2,
            static_cast<UINT>(pixel_width_),
            static_cast<UINT>(pixel_height_),
            DXGI_FORMAT_B8G8R8A8_UNORM,
            0);
        if (FAILED(hr)) {
            set_last_error(format_hresult(L"Resizing swap chain", hr));
            return pv_rendering_render_failed;
        }

        const HRESULT bitmap_result = create_target_bitmap();
        if (FAILED(bitmap_result)) {
            set_last_error(format_hresult(L"Recreating render target bitmap", bitmap_result));
            return pv_rendering_render_failed;
        }

        return render_demo();
    }

    int set_viewport(double start_seconds, double seconds_per_pixel)
    {
        return set_viewport(start_seconds, seconds_per_pixel, true);
    }

    int set_viewport(double start_seconds, double seconds_per_pixel, bool render)
    {
        if (!std::isfinite(start_seconds) || !std::isfinite(seconds_per_pixel) || seconds_per_pixel <= 0.0) {
            set_last_error(L"Viewport start and scale must be finite, and scale must be greater than zero.");
            return pv_rendering_invalid_argument;
        }

        viewport_start_seconds_ = (std::max)(0.0, start_seconds);
        seconds_per_pixel_ = std::clamp(seconds_per_pixel, 1.0e-9, 1.0);

        return render ? render_demo() : pv_rendering_ok;
    }

    int set_channel_counts(int digital_channel_count, int analog_channel_count)
    {
        return set_channel_counts(digital_channel_count, analog_channel_count, true);
    }

    int set_channel_counts(int digital_channel_count, int analog_channel_count, bool render)
    {
        if (digital_channel_count < 0 || analog_channel_count < 0) {
            set_last_error(L"Channel counts must not be negative.");
            return pv_rendering_invalid_argument;
        }

        if (digital_channel_count > 64 || analog_channel_count > 64) {
            set_last_error(L"Channel counts exceed the supported renderer limit.");
            return pv_rendering_invalid_argument;
        }

        digital_channel_count_ = digital_channel_count;
        analog_channel_count_ = analog_channel_count;
        return render ? render_demo() : pv_rendering_ok;
    }

    int set_digital_spans(const pv_rendering_digital_span* spans, int span_count)
    {
        return set_digital_spans(spans, span_count, true);
    }

    int set_digital_spans(const pv_rendering_digital_span* spans, int span_count, bool render)
    {
        if (span_count < 0) {
            set_last_error(L"Digital span count must not be negative.");
            return pv_rendering_invalid_argument;
        }

        if (span_count == 0) {
            digital_spans_.clear();
            has_external_spans_ = true;
            return render ? render_demo() : pv_rendering_ok;
        }

        if (spans == nullptr) {
            set_last_error(L"Digital spans pointer must not be null when span count is greater than zero.");
            return pv_rendering_invalid_argument;
        }

        std::vector<pulseview::core::DigitalSpan> next_spans;
        next_spans.reserve(static_cast<std::size_t>(span_count));
        for (int index = 0; index < span_count; ++index) {
            const auto& span = spans[index];
            next_spans.push_back(pulseview::core::DigitalSpan {
                .x0 = span.x0,
                .x1 = span.x1,
                .level = span.level,
                .edge_flags = span.edge_flags,
                .channel_index = span.channel_index,
            });
        }

        digital_spans_ = std::move(next_spans);
        has_external_spans_ = true;
        return render ? render_demo() : pv_rendering_ok;
    }

    int set_analog_segments(const pv_rendering_analog_segment* segments, int segment_count)
    {
        return set_analog_segments(segments, segment_count, true);
    }

    int set_analog_segments(const pv_rendering_analog_segment* segments, int segment_count, bool render)
    {
        if (segment_count < 0) {
            set_last_error(L"Analog segment count must not be negative.");
            return pv_rendering_invalid_argument;
        }

        if (segment_count == 0) {
            analog_segments_.clear();
            has_external_analog_segments_ = true;
            return render ? render_demo() : pv_rendering_ok;
        }

        if (segments == nullptr) {
            set_last_error(L"Analog segments pointer must not be null when segment count is greater than zero.");
            return pv_rendering_invalid_argument;
        }

        std::vector<AnalogSegment> next_segments;
        next_segments.reserve(static_cast<std::size_t>(segment_count));
        for (int index = 0; index < segment_count; ++index) {
            const auto& segment = segments[index];
            next_segments.push_back(AnalogSegment {
                .x0 = segment.x0,
                .y0 = std::clamp(segment.y0, -1.0F, 1.0F),
                .x1 = segment.x1,
                .y1 = std::clamp(segment.y1, -1.0F, 1.0F),
                .channel_index = segment.channel_index,
                .flags = segment.flags,
            });
        }

        analog_segments_ = std::move(next_segments);
        has_external_analog_segments_ = true;
        return render ? render_demo() : pv_rendering_ok;
    }

    int set_waveform_data(
        int digital_channel_count,
        int analog_channel_count,
        const pv_rendering_digital_span* spans,
        int span_count,
        const pv_rendering_analog_segment* segments,
        int segment_count)
    {
        int status = set_channel_counts(digital_channel_count, analog_channel_count, false);
        if (status != pv_rendering_ok) {
            return status;
        }

        status = set_digital_spans(spans, span_count, false);
        if (status != pv_rendering_ok) {
            return status;
        }

        status = set_analog_segments(segments, segment_count, false);
        if (status != pv_rendering_ok) {
            return status;
        }

        return render_demo();
    }

    int set_viewport_waveform_data(
        double start_seconds,
        double seconds_per_pixel,
        int digital_channel_count,
        int analog_channel_count,
        const pv_rendering_digital_span* spans,
        int span_count,
        const pv_rendering_analog_segment* segments,
        int segment_count)
    {
        int status = set_viewport(start_seconds, seconds_per_pixel, false);
        if (status != pv_rendering_ok) {
            return status;
        }

        return set_waveform_data(
            digital_channel_count,
            analog_channel_count,
            spans,
            span_count,
            segments,
            segment_count);
    }

    int set_decoder_annotations(
        int decoder_row_count,
        const pv_rendering_decoder_annotation* annotations,
        int annotation_count,
        bool render)
    {
        if (decoder_row_count < 0 || annotation_count < 0) {
            set_last_error(L"Decoder row and annotation counts must not be negative.");
            return pv_rendering_invalid_argument;
        }

        if (decoder_row_count > 32) {
            set_last_error(L"Decoder row count exceeds the supported renderer limit.");
            return pv_rendering_invalid_argument;
        }

        if (annotation_count > 0 && annotations == nullptr) {
            set_last_error(L"Decoder annotations pointer must not be null when annotation count is greater than zero.");
            return pv_rendering_invalid_argument;
        }

        decoder_row_count_ = decoder_row_count;
        decoder_annotations_.clear();
        decoder_annotations_.reserve(static_cast<std::size_t>(annotation_count));
        for (int index = 0; index < annotation_count; ++index) {
            const auto& annotation = annotations[index];
            if (annotation.row_index >= decoder_row_count_) {
                continue;
            }

            const auto text_length = bounded_wcslen(annotation.text, 32);
            decoder_annotations_.push_back(DecoderAnnotation {
                .x0 = annotation.x0,
                .x1 = annotation.x1,
                .row_index = annotation.row_index,
                .text = std::wstring(annotation.text, annotation.text + text_length),
            });
        }

        return render ? render_demo() : pv_rendering_ok;
    }

    int set_viewport_waveform_data_ex(
        double start_seconds,
        double seconds_per_pixel,
        int digital_channel_count,
        int analog_channel_count,
        int decoder_row_count,
        const pv_rendering_digital_span* spans,
        int span_count,
        const pv_rendering_analog_segment* segments,
        int segment_count,
        const pv_rendering_decoder_annotation* annotations,
        int annotation_count)
    {
        int status = set_viewport(start_seconds, seconds_per_pixel, false);
        if (status != pv_rendering_ok) {
            return status;
        }

        status = set_decoder_annotations(decoder_row_count, annotations, annotation_count, false);
        if (status != pv_rendering_ok) {
            return status;
        }

        return set_waveform_data(
            digital_channel_count,
            analog_channel_count,
            spans,
            span_count,
            segments,
            segment_count);
    }

    int clear_digital_spans()
    {
        digital_spans_.clear();
        analog_segments_.clear();
        decoder_annotations_.clear();
        has_external_spans_ = false;
        has_external_analog_segments_ = false;
        digital_channel_count_ = 1;
        analog_channel_count_ = 0;
        decoder_row_count_ = 0;
        return render_demo();
    }

    std::wstring_view device_info() const noexcept
    {
        return device_info_;
    }

    int render_demo()
    {
        if (!swap_chain_) {
            return pv_rendering_ok;
        }

        if (!target_bitmap_) {
            const HRESULT bitmap_result = create_target_bitmap();
            if (FAILED(bitmap_result)) {
                set_last_error(format_hresult(L"Creating render target bitmap", bitmap_result));
                return pv_rendering_render_failed;
            }
        }

        HRESULT hr = ensure_brushes();
        if (FAILED(hr)) {
            set_last_error(format_hresult(L"Creating Direct2D brushes", hr));
            return pv_rendering_render_failed;
        }

        const float width = static_cast<float>(pixel_width_) * 96.0F / dpi_;
        const float height = static_cast<float>(pixel_height_) * 96.0F / dpi_;
        constexpr float left_margin = waveform_left_margin;
        constexpr float right_margin = waveform_right_margin;
        const float drawing_width = (std::max)(1.0F, width - left_margin - right_margin);

        d2d_context_->BeginDraw();
        d2d_context_->SetAntialiasMode(D2D1_ANTIALIAS_MODE_ALIASED);
        d2d_context_->Clear(D2D1::ColorF(0x0F172A));

        const double grid_seconds = choose_grid_seconds(drawing_width);
        const double first_grid = std::floor(viewport_start_seconds_ / grid_seconds) * grid_seconds;
        for (double grid_time = first_grid; grid_time < viewport_start_seconds_ + seconds_per_pixel_ * drawing_width; grid_time += grid_seconds) {
            const float x = left_margin + static_cast<float>((grid_time - viewport_start_seconds_) / seconds_per_pixel_);
            if (x >= 0.0F && x <= width) {
                d2d_context_->DrawLine(D2D1::Point2F(x + 0.5F, 0.0F), D2D1::Point2F(x + 0.5F, height), grid_brush_.Get(), 1.0F);
            }
        }

        for (float y = 0.5F; y < height; y += 40.0F) {
            d2d_context_->DrawLine(D2D1::Point2F(0.0F, y), D2D1::Point2F(width, y), grid_brush_.Get(), 1.0F);
        }

        draw_track_grid(grid_brush_.Get(), axis_brush_.Get(), left_margin, width - right_margin, height);
        draw_digital_spans(waveform_brush_.Get(), left_margin, drawing_width, height);
        d2d_context_->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        draw_analog_segments(analog_brush_.Get(), left_margin, drawing_width, height);
        d2d_context_->SetAntialiasMode(D2D1_ANTIALIAS_MODE_ALIASED);
        draw_decoder_annotations(left_margin, drawing_width, height);

        hr = d2d_context_->EndDraw();
        if (hr == D2DERR_RECREATE_TARGET) {
            target_bitmap_.Reset();
            grid_brush_.Reset();
            axis_brush_.Reset();
            waveform_brush_.Reset();
            analog_brush_.Reset();
            decoder_fill_brush_.Reset();
            decoder_border_brush_.Reset();
            return render_demo();
        }

        if (FAILED(hr)) {
            set_last_error(format_hresult(L"Drawing demo waveform", hr));
            return pv_rendering_render_failed;
        }

        hr = swap_chain_->Present(1, 0);
        if (FAILED(hr)) {
            set_last_error(format_hresult(L"Presenting swap chain", hr));
            return pv_rendering_render_failed;
        }

        return pv_rendering_ok;
    }

private:
    HRESULT ensure_device()
    {
        if (d3d_device_ && d2d_context_) {
            return S_OK;
        }

        constexpr D3D_FEATURE_LEVEL feature_levels[] = {
            D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL_11_0,
            D3D_FEATURE_LEVEL_10_1,
            D3D_FEATURE_LEVEL_10_0,
        };

        D3D_FEATURE_LEVEL feature_level = D3D_FEATURE_LEVEL_11_0;
        ComPtr<ID3D11DeviceContext> d3d_context;
        bool using_warp = false;
        HRESULT hr = D3D11CreateDevice(
            nullptr,
            D3D_DRIVER_TYPE_HARDWARE,
            nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            feature_levels,
            static_cast<UINT>(std::size(feature_levels)),
            D3D11_SDK_VERSION,
            &d3d_device_,
            &feature_level,
            &d3d_context);
        if (FAILED(hr)) {
            hr = D3D11CreateDevice(
                nullptr,
                D3D_DRIVER_TYPE_WARP,
                nullptr,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                feature_levels,
                static_cast<UINT>(std::size(feature_levels)),
                D3D11_SDK_VERSION,
                &d3d_device_,
                &feature_level,
                &d3d_context);
            using_warp = SUCCEEDED(hr);
        }

        if (FAILED(hr)) {
            return hr;
        }

        ComPtr<IDXGIDevice1> dxgi_device1;
        if (SUCCEEDED(d3d_device_.As(&dxgi_device1))) {
            dxgi_device1->SetMaximumFrameLatency(1);
        }

        D2D1_FACTORY_OPTIONS factory_options = {};
        hr = D2D1CreateFactory(
            D2D1_FACTORY_TYPE_SINGLE_THREADED,
            __uuidof(ID2D1Factory1),
            &factory_options,
            reinterpret_cast<void**>(d2d_factory_.ReleaseAndGetAddressOf()));
        if (FAILED(hr)) {
            return hr;
        }

        ComPtr<IDXGIDevice> dxgi_device;
        hr = d3d_device_.As(&dxgi_device);
        if (FAILED(hr)) {
            return hr;
        }

        ComPtr<IDXGIAdapter> adapter;
        if (SUCCEEDED(dxgi_device->GetAdapter(&adapter))) {
            DXGI_ADAPTER_DESC description = {};
            if (SUCCEEDED(adapter->GetDesc(&description))) {
                device_info_ = using_warp ? L"Direct3D WARP adapter: " : L"Direct3D hardware adapter: ";
                device_info_ += description.Description;
                device_info_ += L", feature level ";
                device_info_ += feature_level_name(feature_level);
            }
        }

        hr = d2d_factory_->CreateDevice(dxgi_device.Get(), &d2d_device_);
        if (FAILED(hr)) {
            return hr;
        }

        return d2d_device_->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &d2d_context_);
    }

    HRESULT ensure_brushes()
    {
        if (grid_brush_ && axis_brush_ && waveform_brush_ && analog_brush_ && decoder_fill_brush_ && decoder_border_brush_) {
            return S_OK;
        }

        HRESULT hr = d2d_context_->CreateSolidColorBrush(D2D1::ColorF(0x243447), &grid_brush_);
        if (FAILED(hr)) {
            return hr;
        }

        hr = d2d_context_->CreateSolidColorBrush(D2D1::ColorF(0x64748B), &axis_brush_);
        if (FAILED(hr)) {
            return hr;
        }

        hr = d2d_context_->CreateSolidColorBrush(D2D1::ColorF(0x22C55E), &waveform_brush_);
        if (FAILED(hr)) {
            return hr;
        }

        hr = d2d_context_->CreateSolidColorBrush(D2D1::ColorF(0x38BDF8), &analog_brush_);
        if (FAILED(hr)) {
            return hr;
        }

        hr = d2d_context_->CreateSolidColorBrush(D2D1::ColorF(0x334155, 0.86F), &decoder_fill_brush_);
        if (FAILED(hr)) {
            return hr;
        }

        return d2d_context_->CreateSolidColorBrush(D2D1::ColorF(0xFACC15), &decoder_border_brush_);
    }

    HRESULT create_swap_chain()
    {
        ComPtr<IDXGIDevice> dxgi_device;
        HRESULT hr = d3d_device_.As(&dxgi_device);
        if (FAILED(hr)) {
            return hr;
        }

        ComPtr<IDXGIAdapter> adapter;
        hr = dxgi_device->GetAdapter(&adapter);
        if (FAILED(hr)) {
            return hr;
        }

        ComPtr<IDXGIFactory2> dxgi_factory;
        hr = adapter->GetParent(IID_PPV_ARGS(&dxgi_factory));
        if (FAILED(hr)) {
            return hr;
        }

        DXGI_SWAP_CHAIN_DESC1 description = {};
        description.Width = static_cast<UINT>(pixel_width_);
        description.Height = static_cast<UINT>(pixel_height_);
        description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        description.Stereo = FALSE;
        description.SampleDesc.Count = 1;
        description.SampleDesc.Quality = 0;
        description.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        description.BufferCount = 2;
        description.Scaling = DXGI_SCALING_STRETCH;
        description.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
        description.AlphaMode = DXGI_ALPHA_MODE_IGNORE;

        swap_chain_.Reset();
        target_bitmap_.Reset();
        d2d_context_->SetTarget(nullptr);

        hr = dxgi_factory->CreateSwapChainForComposition(d3d_device_.Get(), &description, nullptr, &swap_chain_);
        if (FAILED(hr)) {
            return hr;
        }

        return create_target_bitmap();
    }

    HRESULT create_target_bitmap()
    {
        ComPtr<IDXGISurface> dxgi_surface;
        HRESULT hr = swap_chain_->GetBuffer(0, IID_PPV_ARGS(&dxgi_surface));
        if (FAILED(hr)) {
            return hr;
        }

        const D2D1_BITMAP_PROPERTIES1 properties = D2D1::BitmapProperties1(
            D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_IGNORE),
            dpi_,
            dpi_);

        hr = d2d_context_->CreateBitmapFromDxgiSurface(dxgi_surface.Get(), &properties, &target_bitmap_);
        if (FAILED(hr)) {
            return hr;
        }

        d2d_context_->SetTarget(target_bitmap_.Get());
        return S_OK;
    }

    double choose_grid_seconds(float drawing_width) const
    {
        const double visible_seconds = seconds_per_pixel_ * static_cast<double>(drawing_width);
        const double target_seconds = (std::max)(visible_seconds / 8.0, seconds_per_pixel_ * 48.0);
        const double exponent = std::floor(std::log10(target_seconds));
        const double base = std::pow(10.0, exponent);
        const double normalized = target_seconds / base;

        if (normalized <= 2.0) {
            return 2.0 * base;
        }

        if (normalized <= 5.0) {
            return 5.0 * base;
        }

        return 10.0 * base;
    }

    int total_track_count() const
    {
        if (!has_external_spans_ && !has_external_analog_segments_ && decoder_row_count_ == 0) {
            return 1;
        }

        return (std::max)(1, digital_channel_count_ + analog_channel_count_ + decoder_row_count_);
    }

    float track_top(int track_index, float height) const
    {
        constexpr float top_margin = 18.0F;
        constexpr float bottom_margin = 14.0F;
        const float available_height = (std::max)(1.0F, height - top_margin - bottom_margin);
        return top_margin + available_height * static_cast<float>(track_index) / static_cast<float>(total_track_count());
    }

    float track_bottom(int track_index, float height) const
    {
        constexpr float top_margin = 18.0F;
        constexpr float bottom_margin = 14.0F;
        const float available_height = (std::max)(1.0F, height - top_margin - bottom_margin);
        return top_margin + available_height * static_cast<float>(track_index + 1) / static_cast<float>(total_track_count());
    }

    void draw_track_grid(ID2D1Brush* grid_brush, ID2D1Brush* axis_brush, float left, float right, float height)
    {
        const int tracks = total_track_count();
        for (int track = 0; track < tracks; ++track) {
            const float top = track_top(track, height);
            const float bottom = track_bottom(track, height);
            const float center = (top + bottom) * 0.5F;

            d2d_context_->DrawLine(D2D1::Point2F(left, top), D2D1::Point2F(right, top), grid_brush, 1.0F);
            d2d_context_->DrawLine(D2D1::Point2F(left, center), D2D1::Point2F(right, center), axis_brush, 0.75F);
        }

        d2d_context_->DrawLine(
            D2D1::Point2F(left, track_bottom(tracks - 1, height)),
            D2D1::Point2F(right, track_bottom(tracks - 1, height)),
            grid_brush,
            1.0F);
    }

    void draw_decoder_annotations(float left, float width, float height)
    {
        if (decoder_row_count_ <= 0 || !decoder_fill_brush_ || !decoder_border_brush_) {
            return;
        }

        for (const auto& annotation : decoder_annotations_) {
            const int row = static_cast<int>(annotation.row_index);
            if (row < 0 || row >= decoder_row_count_) {
                continue;
            }

            const int track = digital_channel_count_ + analog_channel_count_ + row;
            const float top = track_top(track, height) + 5.0F;
            const float bottom = track_bottom(track, height) - 5.0F;
            const float x0 = left + std::clamp(annotation.x0, 0.0F, width);
            const float x1 = left + std::clamp(annotation.x1, 0.0F, width);
            const float right = (std::min)(left + width, (std::max)(x1, x0 + 3.0F));
            if (right <= x0) {
                continue;
            }

            const auto rect = D2D1::RectF(x0, top, right, (std::max)(bottom, top + 18.0F));

            d2d_context_->FillRectangle(rect, decoder_fill_brush_.Get());
            d2d_context_->DrawRectangle(rect, decoder_border_brush_.Get(), 1.0F);
        }
    }

    void draw_digital_spans(ID2D1Brush* waveform_brush, float left, float width, float height)
    {
        const pulseview::core::ViewportRequest request {
            .start_seconds = viewport_start_seconds_,
            .seconds_per_pixel = seconds_per_pixel_,
            .width_pixels = width,
        };
        const auto queried_spans = pulseview::core::query_synthetic_digital_spans(request);
        const auto& spans = has_external_spans_ ? digital_spans_ : queried_spans;

        struct PointMarker {
            float x;
            float y;
        };
        std::vector<PointMarker> points;
        points.reserve(128);

        for (const auto& span : spans) {
            const int channel = has_external_spans_ ? static_cast<int>(span.channel_index) : 0;
            if (channel < 0 || channel >= (std::max)(1, digital_channel_count_)) {
                continue;
            }

            const float top = track_top(channel, height);
            const float bottom = track_bottom(channel, height);
            const float center = (top + bottom) * 0.5F;
            const float amplitude = (std::max)(4.0F, (bottom - top) * 0.28F);
            const float high_y = center - amplitude;
            const float low_y = center + amplitude;
            const float x0 = left + std::clamp(span.x0, 0.0F, width);
            const float x1 = left + std::clamp(span.x1, 0.0F, width);
            const float y = span.level != 0 ? high_y : low_y;
            if ((span.edge_flags & digital_sample_point) != 0) {
                points.push_back(PointMarker { .x = x0, .y = y });
                continue;
            }

            if ((span.edge_flags & dense_digital_span) != 0) {
                const float top_y = (std::min)(high_y, low_y);
                const float bottom_y = (std::max)(high_y, low_y);
                d2d_context_->FillRectangle(D2D1::RectF(x0, top_y, x1, bottom_y), waveform_brush);
                continue;
            }

            d2d_context_->DrawLine(D2D1::Point2F(x0, y), D2D1::Point2F(x1, y), waveform_brush, 2.0F);

            if ((span.edge_flags & pulseview::core::digital_edge_rising) != 0) {
                d2d_context_->DrawLine(D2D1::Point2F(x0, low_y), D2D1::Point2F(x0, high_y), waveform_brush, 2.0F);
            }
            else if ((span.edge_flags & pulseview::core::digital_edge_falling) != 0) {
                d2d_context_->DrawLine(D2D1::Point2F(x0, high_y), D2D1::Point2F(x0, low_y), waveform_brush, 2.0F);
            }
        }

        if (!points.empty()) {
            d2d_context_->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
            for (const auto& point : points) {
                constexpr float radius = 2.45F;
                d2d_context_->FillEllipse(
                    D2D1::Ellipse(D2D1::Point2F(point.x, point.y), radius, radius),
                    waveform_brush);
            }

            d2d_context_->SetAntialiasMode(D2D1_ANTIALIAS_MODE_ALIASED);
        }
    }

    void draw_analog_segments(ID2D1Brush* analog_brush, float left, float width, float height)
    {
        if (!has_external_analog_segments_ || analog_channel_count_ <= 0) {
            return;
        }

        struct PointMarker {
            float x;
            float y;
        };
        std::vector<PointMarker> points;
        points.reserve(128);

        for (const auto& segment : analog_segments_) {
            const int channel = static_cast<int>(segment.channel_index);
            if (channel < 0 || channel >= analog_channel_count_) {
                continue;
            }

            const int track = digital_channel_count_ + channel;
            const float top = track_top(track, height);
            const float bottom = track_bottom(track, height);
            const float center = (top + bottom) * 0.5F;
            const float amplitude = (std::max)(4.0F, (bottom - top) * 0.36F);
            const float x0 = left + std::clamp(segment.x0, 0.0F, width);
            const float x1 = left + std::clamp(segment.x1, 0.0F, width);
            const float value0 = std::clamp(segment.y0, -1.0F, 1.0F);
            const float value1 = std::clamp(segment.y1, -1.0F, 1.0F);
            const float y0 = center - value0 * amplitude;
            const float y1 = center - value1 * amplitude;

            if ((segment.flags & analog_segment_point) != 0) {
                points.push_back(PointMarker { .x = x0, .y = y0 });
            }
            else if ((segment.flags & analog_segment_envelope) != 0) {
                const float min_value = (std::min)(value0, value1);
                const float max_value = (std::max)(value0, value1);
                const float top_y = center - max_value * amplitude;
                float bottom_y = center - min_value * amplitude;
                if (bottom_y - top_y < 1.0F) {
                    bottom_y = top_y + 1.0F;
                }

                const float rect_right = (std::max)(x1, x0 + 1.0F);
                d2d_context_->FillRectangle(D2D1::RectF(x0, top_y, rect_right, bottom_y), analog_brush);
            }
            else if (x1 > x0) {
                d2d_context_->DrawLine(D2D1::Point2F(x0, y0), D2D1::Point2F(x1, y1), analog_brush, 1.35F);
            }
        }

        for (const auto& point : points) {
            constexpr float radius = 2.35F;
            d2d_context_->FillEllipse(
                D2D1::Ellipse(D2D1::Point2F(point.x, point.y), radius, radius),
                analog_brush);
        }
    }

    int pixel_width_ = 640;
    int pixel_height_ = 360;
    float dpi_ = 96.0F;
    double viewport_start_seconds_ = 0.0;
    double seconds_per_pixel_ = 10.0e-6;
    int digital_channel_count_ = 1;
    int analog_channel_count_ = 0;
    int decoder_row_count_ = 0;
    bool has_external_spans_ = false;
    bool has_external_analog_segments_ = false;
    std::vector<pulseview::core::DigitalSpan> digital_spans_;
    std::vector<AnalogSegment> analog_segments_;
    std::vector<DecoderAnnotation> decoder_annotations_;
    std::wstring device_info_ = L"Direct3D device not initialized.";
    ComPtr<ISwapChainPanelNative> panel_native_;
    ComPtr<ID3D11Device> d3d_device_;
    ComPtr<ID2D1Factory1> d2d_factory_;
    ComPtr<ID2D1Device> d2d_device_;
    ComPtr<ID2D1DeviceContext> d2d_context_;
    ComPtr<IDXGISwapChain1> swap_chain_;
    ComPtr<ID2D1Bitmap1> target_bitmap_;
    ComPtr<ID2D1SolidColorBrush> grid_brush_;
    ComPtr<ID2D1SolidColorBrush> axis_brush_;
    ComPtr<ID2D1SolidColorBrush> waveform_brush_;
    ComPtr<ID2D1SolidColorBrush> analog_brush_;
    ComPtr<ID2D1SolidColorBrush> decoder_fill_brush_;
    ComPtr<ID2D1SolidColorBrush> decoder_border_brush_;
};

} // namespace

struct pv_renderer_handle {
    Renderer renderer;
};

extern "C" PV_RENDERING_API int pv_rendering_get_version(wchar_t* buffer, int buffer_length)
{
    constexpr std::wstring_view version = L"PulseView.Rendering.Native 0.1.0";
    return copy_wide_string(version, buffer, buffer_length);
}

extern "C" PV_RENDERING_API int pv_rendering_get_last_error(wchar_t* buffer, int buffer_length)
{
    return copy_wide_string(last_error, buffer, buffer_length);
}

extern "C" PV_RENDERING_API pv_renderer_handle* pv_renderer_create()
{
    try {
        auto* renderer = new (std::nothrow) pv_renderer_handle();
        if (renderer == nullptr) {
            set_last_error(L"Failed to allocate renderer.");
        }

        return renderer;
    }
    catch (...) {
        set_last_error(L"Unexpected exception while creating renderer.");
        return nullptr;
    }
}

extern "C" PV_RENDERING_API void pv_renderer_destroy(pv_renderer_handle* renderer)
{
    delete renderer;
}

extern "C" PV_RENDERING_API int pv_renderer_attach_swap_chain_panel(
    pv_renderer_handle* renderer,
    void* swap_chain_panel_unknown)
{
    if (renderer == nullptr) {
        set_last_error(L"Renderer handle must not be null.");
        return pv_rendering_invalid_argument;
    }

    try {
        return renderer->renderer.attach_swap_chain_panel(swap_chain_panel_unknown);
    }
    catch (...) {
        set_last_error(L"Unexpected exception while attaching renderer.");
        return pv_rendering_unexpected_error;
    }
}

extern "C" PV_RENDERING_API int pv_renderer_resize(
    pv_renderer_handle* renderer,
    int pixel_width,
    int pixel_height,
    float dpi)
{
    if (renderer == nullptr) {
        set_last_error(L"Renderer handle must not be null.");
        return pv_rendering_invalid_argument;
    }

    try {
        return renderer->renderer.resize(pixel_width, pixel_height, dpi);
    }
    catch (...) {
        set_last_error(L"Unexpected exception while resizing renderer.");
        return pv_rendering_unexpected_error;
    }
}

extern "C" PV_RENDERING_API int pv_renderer_set_viewport(
    pv_renderer_handle* renderer,
    double start_seconds,
    double seconds_per_pixel)
{
    if (renderer == nullptr) {
        set_last_error(L"Renderer handle must not be null.");
        return pv_rendering_invalid_argument;
    }

    try {
        return renderer->renderer.set_viewport(start_seconds, seconds_per_pixel);
    }
    catch (...) {
        set_last_error(L"Unexpected exception while setting viewport.");
        return pv_rendering_unexpected_error;
    }
}

extern "C" PV_RENDERING_API int pv_renderer_set_channel_counts(
    pv_renderer_handle* renderer,
    int digital_channel_count,
    int analog_channel_count)
{
    if (renderer == nullptr) {
        set_last_error(L"Renderer handle must not be null.");
        return pv_rendering_invalid_argument;
    }

    try {
        return renderer->renderer.set_channel_counts(digital_channel_count, analog_channel_count);
    }
    catch (...) {
        set_last_error(L"Unexpected exception while setting channel counts.");
        return pv_rendering_unexpected_error;
    }
}

extern "C" PV_RENDERING_API int pv_renderer_set_digital_spans(
    pv_renderer_handle* renderer,
    const pv_rendering_digital_span* spans,
    int span_count)
{
    if (renderer == nullptr) {
        set_last_error(L"Renderer handle must not be null.");
        return pv_rendering_invalid_argument;
    }

    try {
        return renderer->renderer.set_digital_spans(spans, span_count);
    }
    catch (const std::bad_alloc&) {
        set_last_error(L"Not enough memory to set digital spans.");
        return pv_rendering_unexpected_error;
    }
    catch (...) {
        set_last_error(L"Unexpected exception while setting digital spans.");
        return pv_rendering_unexpected_error;
    }
}

extern "C" PV_RENDERING_API int pv_renderer_set_analog_segments(
    pv_renderer_handle* renderer,
    const pv_rendering_analog_segment* segments,
    int segment_count)
{
    if (renderer == nullptr) {
        set_last_error(L"Renderer handle must not be null.");
        return pv_rendering_invalid_argument;
    }

    try {
        return renderer->renderer.set_analog_segments(segments, segment_count);
    }
    catch (const std::bad_alloc&) {
        set_last_error(L"Not enough memory to set analog segments.");
        return pv_rendering_unexpected_error;
    }
    catch (...) {
        set_last_error(L"Unexpected exception while setting analog segments.");
        return pv_rendering_unexpected_error;
    }
}

extern "C" PV_RENDERING_API int pv_renderer_set_waveform_data(
    pv_renderer_handle* renderer,
    int digital_channel_count,
    int analog_channel_count,
    const pv_rendering_digital_span* spans,
    int span_count,
    const pv_rendering_analog_segment* segments,
    int segment_count)
{
    if (renderer == nullptr) {
        set_last_error(L"Renderer handle must not be null.");
        return pv_rendering_invalid_argument;
    }

    try {
        return renderer->renderer.set_waveform_data(
            digital_channel_count,
            analog_channel_count,
            spans,
            span_count,
            segments,
            segment_count);
    }
    catch (const std::bad_alloc&) {
        set_last_error(L"Not enough memory to set waveform data.");
        return pv_rendering_unexpected_error;
    }
    catch (...) {
        set_last_error(L"Unexpected exception while setting waveform data.");
        return pv_rendering_unexpected_error;
    }
}

extern "C" PV_RENDERING_API int pv_renderer_set_viewport_waveform_data(
    pv_renderer_handle* renderer,
    double start_seconds,
    double seconds_per_pixel,
    int digital_channel_count,
    int analog_channel_count,
    const pv_rendering_digital_span* spans,
    int span_count,
    const pv_rendering_analog_segment* segments,
    int segment_count)
{
    if (renderer == nullptr) {
        set_last_error(L"Renderer handle must not be null.");
        return pv_rendering_invalid_argument;
    }

    try {
        return renderer->renderer.set_viewport_waveform_data(
            start_seconds,
            seconds_per_pixel,
            digital_channel_count,
            analog_channel_count,
            spans,
            span_count,
            segments,
            segment_count);
    }
    catch (const std::bad_alloc&) {
        set_last_error(L"Not enough memory to set viewport waveform data.");
        return pv_rendering_unexpected_error;
    }
    catch (...) {
        set_last_error(L"Unexpected exception while setting viewport waveform data.");
        return pv_rendering_unexpected_error;
    }
}

extern "C" PV_RENDERING_API int pv_renderer_set_viewport_waveform_data_ex(
    pv_renderer_handle* renderer,
    double start_seconds,
    double seconds_per_pixel,
    int digital_channel_count,
    int analog_channel_count,
    int decoder_row_count,
    const pv_rendering_digital_span* spans,
    int span_count,
    const pv_rendering_analog_segment* segments,
    int segment_count,
    const pv_rendering_decoder_annotation* annotations,
    int annotation_count)
{
    if (renderer == nullptr) {
        set_last_error(L"Renderer handle must not be null.");
        return pv_rendering_invalid_argument;
    }

    try {
        return renderer->renderer.set_viewport_waveform_data_ex(
            start_seconds,
            seconds_per_pixel,
            digital_channel_count,
            analog_channel_count,
            decoder_row_count,
            spans,
            span_count,
            segments,
            segment_count,
            annotations,
            annotation_count);
    }
    catch (const std::bad_alloc&) {
        set_last_error(L"Not enough memory to set viewport waveform data.");
        return pv_rendering_unexpected_error;
    }
    catch (...) {
        set_last_error(L"Unexpected exception while setting viewport waveform data.");
        return pv_rendering_unexpected_error;
    }
}

extern "C" PV_RENDERING_API int pv_renderer_get_device_info(
    pv_renderer_handle* renderer,
    wchar_t* buffer,
    int buffer_length)
{
    if (renderer == nullptr) {
        set_last_error(L"Renderer handle must not be null.");
        return pv_rendering_invalid_argument;
    }

    return copy_wide_string(renderer->renderer.device_info(), buffer, buffer_length);
}

extern "C" PV_RENDERING_API int pv_renderer_clear_digital_spans(pv_renderer_handle* renderer)
{
    if (renderer == nullptr) {
        set_last_error(L"Renderer handle must not be null.");
        return pv_rendering_invalid_argument;
    }

    try {
        return renderer->renderer.clear_digital_spans();
    }
    catch (...) {
        set_last_error(L"Unexpected exception while clearing digital spans.");
        return pv_rendering_unexpected_error;
    }
}

extern "C" PV_RENDERING_API int pv_renderer_render_demo(pv_renderer_handle* renderer)
{
    if (renderer == nullptr) {
        set_last_error(L"Renderer handle must not be null.");
        return pv_rendering_invalid_argument;
    }

    try {
        return renderer->renderer.render_demo();
    }
    catch (...) {
        set_last_error(L"Unexpected exception while rendering.");
        return pv_rendering_unexpected_error;
    }
}
