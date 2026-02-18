// Stock Adjustment Request - JavaScript
(function ()
{
    'use strict';

    // Global variables
    let gridData = [];
    let nextStockRecSno = 1;
    let uploadedFiles = {};

    // Cached API data
    let cachedItems = [];
    let cachedLocations = [];
    let itemsLoaded = false;
    let locationsLoaded = false;

    // Default location from App Options (for Stock Decrease)
    let defaultAppLocation = '';

    // Initialize on document ready
    $(document).ready(function () {
        initializeEvents();
        loadMasterDataFromApi();
        loadDefaultAppLocation();
    });

    // ==================== SPINNER HELPERS ====================

    function showSpinner(message) {
        $('#spinnerMessage').text(message || 'Loading...');
        $('#globalSpinner').css('display', 'flex');
    }

    function hideSpinner() {
        $('#globalSpinner').hide();
    }

    // ==================== LOAD MASTER DATA FROM SAGE API ====================

    function loadMasterDataFromApi()
    {
        showSpinner('Loading items and locations from Sage...');

        var itemsPromise = loadItemsFromApi();
        var locationsPromise = loadLocationsFromApi();

        $.when(itemsPromise, locationsPromise).always(function () {
            hideSpinner();
        });
    }

    function loadItemsFromApi()
    {
        return $.ajax({
            url: appBasePath + '/StockAdjustment/SearchItems',
            type: 'GET',
            data: { term: '' },
            success: function (response)
            {
                if (response.success && response.results) {
                    cachedItems = response.results;
                    itemsLoaded = true;
                    populateItemDropdown(cachedItems);
                } else {
                    showAlert(response.message || 'Failed to load items from Sage API', 'error');
                }
            },
            error: function () {
                showAlert('Error connecting to Sage API for items', 'error');
            }
        });
    }

    function loadLocationsFromApi()
    {
        return $.ajax({
            url: appBasePath + '/StockAdjustment/SearchLocations',
            type: 'GET',
            data: { term: '' },
            success: function (response)
            {
                if (response.success && response.results) {
                    cachedLocations = response.results;
                    locationsLoaded = true;
                    populateLocationDropdown(cachedLocations);
                } else {
                    showAlert(response.message || 'Failed to load locations from Sage API', 'error');
                }
            },
            error: function () {
                showAlert('Error connecting to Sage API for locations', 'error');
            }
        });
    }

    // Load default location from App Options (used for Stock Decrease To Location)
    function loadDefaultAppLocation()
    {
        $.ajax({
            url: appBasePath + '/StockAdjustment/GetDefaultLocation',
            type: 'POST',
            success: function (response)
            {
                if (response.success) {
                    defaultAppLocation = response.location;
                }
            }
        });
    }

    function populateItemDropdown(items)
    {
        var $select = $('#modalItemCode');
        $select.empty();
        $select.append('<option value="">-- Select Item --</option>');

        $.each(items, function (i, item) {
            $select.append('<option value="' + escapeHtml(item.id) + '">' + escapeHtml(item.text) + '</option>');
        });
    }

    function populateLocationDropdown(locations)
    {
        var $select = $('#modalLocation');
        $select.empty();
        $select.append('<option value="">-- Select Location --</option>');

        $.each(locations, function (i, loc) {
            $select.append('<option value="' + escapeHtml(loc.id) + '">' + escapeHtml(loc.text) + '</option>');
        });
    }

    // Get display text for an item code from cached items
    function getItemDisplayText(itemCode)
    {
        if (!itemCode) return '';
        var found = cachedItems.find(function (i) { return i.id === itemCode; });
        return found ? found.text : itemCode;
    }

    // Get display text for a location code from cached locations
    function getLocationDisplayText(locationCode)
    {
        if (!locationCode) return '';
        var found = cachedLocations.find(function (l) { return l.id === locationCode; });
        return found ? found.text : locationCode;
    }

    // ==================== SELECT2 INITIALIZATION ====================

    function initSelect2OnModal()
    {
        // Destroy existing Select2 instances if any
        if ($('#modalRecType').hasClass('select2-hidden-accessible')) {
            $('#modalRecType').select2('destroy');
        }
        if ($('#modalItemCode').hasClass('select2-hidden-accessible')) {
            $('#modalItemCode').select2('destroy');
        }
        if ($('#modalLocation').hasClass('select2-hidden-accessible')) {
            $('#modalLocation').select2('destroy');
        }

        var $modal = $('#itemModal');

        $('#modalRecType').select2({
            theme: 'bootstrap-5',
            placeholder: '-- Select Type --',
            allowClear: true,
            dropdownParent: $modal,
            width: '100%'
        });

        $('#modalItemCode').select2({
            theme: 'bootstrap-5',
            placeholder: '-- Select Item --',
            allowClear: true,
            dropdownParent: $modal,
            width: '100%'
        });

        $('#modalLocation').select2({
            theme: 'bootstrap-5',
            placeholder: '-- Select Location --',
            allowClear: true,
            dropdownParent: $modal,
            width: '100%'
        });

        // Bind Select2 change events for item code
        $('#modalItemCode').off('select2:select select2:clear').on('select2:select select2:clear', function () {
            loadItemDescription($(this).val());
            loadStockQty();
        });

        // Bind Select2 change events for location (to fetch stock qty)
        $('#modalLocation').off('select2:select select2:clear').on('select2:select select2:clear', function () {
            loadStockQty();
        });
    }

    function initializeEvents()
    {
        // Add Item button
        $('#btnAddItem').on('click', function () {
            openItemModal();
        });

        // Save Item button in modal
        $('#btnSaveItem').on('click', function () {
            saveItem();
        });

        // Item Code selection - auto-fill description (delegated for dynamically populated dropdown)
        $(document).on('change', '#modalItemCode', function () {
            loadItemDescription($(this).val());
            loadStockQty();
        });

        // Location selection - fetch stock qty
        $(document).on('change', '#modalLocation', function () {
            loadStockQty();
        });

        // File upload change events
        $('.file-upload').on('change', function () {
            handleFileUpload($(this));
        });

        // Clear file button
        $('.btn-clear-file').on('click', function () {
            clearFile($(this).data('file-id'));
        });

        // Review Data button
        $('#btnReviewData').on('click', function () {
            reviewData();
        });

        // Import Excel button
        $('#btnImportExcel').on('click', function () {
            $('#importFileInput').click();
        });

        // Import file selected
        $('#importFileInput').on('change', function ()
        {
            var file = this.files[ 0 ];
            if (file) {
                importExcelFile(file);
                $(this).val('');
            }
        });
    }

    // Handle file upload
    function handleFileUpload($input)
    {
        var fileTypeId = $input.data('file-type');
        var maxSize = $input.data('max-size'); // in KB
        var file = $input[ 0 ].files[ 0 ];

        if (!file) return;

        var fileSizeKB = file.size / 1024;
        if (fileSizeKB > maxSize) {
            showAlert('File size exceeds maximum limit of ' + (maxSize / 1024) + ' MB', 'warning');
            $input.val('');
            return;
        }

        uploadedFiles[ fileTypeId ] = file;
        $('#fileName_' + fileTypeId).text(file.name + ' (' + (fileSizeKB / 1024).toFixed(2) + ' MB)');
        $('.btn-clear-file[data-file-id="' + fileTypeId + '"]').show();
    }

    // Clear file
    function clearFile(fileTypeId) {
        delete uploadedFiles[ fileTypeId ];
        $('#file_' + fileTypeId).val('');
        $('#fileName_' + fileTypeId).text('');
        $('.btn-clear-file[data-file-id="' + fileTypeId + '"]').hide();
    }

    // Review Data
    function reviewData()
    {
        if (gridData.length === 0) {
            showAlert('Please add at least one item', 'warning');
            return;
        }

        var missingFiles = [];
        $('.file-upload').each(function ()
        {
            var $input = $(this);
            var isRequired = $input.data('required') === true || $input.data('required') === 'true';
            var fileTypeId = $input.data('file-type');
            var label = $input.closest('.mb-3').find('label').text().trim();

            if (isRequired && !uploadedFiles[ fileTypeId ]) {
                missingFiles.push(label.split('*')[ 0 ].trim());
            }
        });

        if (missingFiles.length > 0) {
            showAlert('Please upload required files: ' + missingFiles.join(', '), 'warning');
            return;
        }

        showReviewModal();
    }

    // Show Review Modal
    function showReviewModal()
    {
        var itemsHtml = '';
        gridData.forEach(function (item, index) {
            itemsHtml += '<tr>' +
                '<td>' + (index + 1) + '</td>' +
                '<td>' + escapeHtml(item.recTypeName) + '</td>' +
                '<td>' + escapeHtml(item.itemCode) + '</td>' +
                '<td>' + escapeHtml(item.itemDescription) + '</td>' +
                '<td>' + escapeHtml(item.locationName) + '</td>' +
                '<td>' + (item.stockOnHand ? parseFloat(item.stockOnHand).toFixed(3) : '0.000') + '</td>' +
                '<td>' + item.qty.toFixed(3) + '</td>' +
                '<td>' + (item.cost ? parseFloat(item.cost).toFixed(4) : '0.0000') + '</td>' +
                '</tr>';
        });

        var filesHtml = '';
        for (var fileTypeId in uploadedFiles) {
            var file = uploadedFiles[ fileTypeId ];
            filesHtml += '<li>' + escapeHtml(file.name) + ' (' + (file.size / 1024 / 1024).toFixed(2) + ' MB)</li>';
        }

        var reviewHtml = '<div class="modal fade" id="reviewModal" tabindex="-1" data-bs-backdrop="static" data-bs-keyboard="false">' +
            '<div class="modal-dialog modal-xl"><div class="modal-content">' +
            '<div class="modal-header bg-primary text-white">' +
            '<h5 class="modal-title"><i class="bi bi-eye-fill me-2"></i>Review Stock Adjustment</h5></div>' +
            '<div class="modal-body p-4">' +
            '<h6 class="fw-bold mb-3">Transaction Details</h6>' +
            '<div class="row mb-4">' +
            '<div class="col-md-6"><p><strong>Request Date:</strong> ' + $('#requestDate').val() + '</p></div>' +
            '<div class="col-md-6"><p><strong>Requestor:</strong> ' + $('#requestor').val() + '</p></div></div>' +
            '<h6 class="fw-bold mb-3">Items (' + gridData.length + ')</h6>' +
            '<div class="table-responsive mb-4"><table class="table table-bordered table-sm">' +
            '<thead class="table-light"><tr>' +
            '<th>Sr No</th><th>Adj Type</th><th>Item Code</th><th>Description</th><th>Location</th><th>Stock On Hand</th><th>Qty</th><th>Cost</th>' +
            '</tr></thead><tbody>' + itemsHtml + '</tbody></table></div>' +
            '<h6 class="fw-bold mb-3">Attachments (' + Object.keys(uploadedFiles).length + ')</h6>' +
            '<ul class="list-unstyled">' + filesHtml + '</ul>' +
            '</div><div class="modal-footer">' +
            '<button type="button" class="btn btn-secondary" data-bs-dismiss="modal"><i class="bi bi-arrow-left me-2"></i>Back to Edit</button>' +
            '<button type="button" class="btn btn-success" id="btnConfirmSubmit"><i class="bi bi-check-circle me-2"></i>Confirm & Submit</button>' +
            '</div></div></div></div>';

        $('#reviewModal').remove();
        $('body').append(reviewHtml);

        var reviewModal = new bootstrap.Modal(document.getElementById('reviewModal'));
        reviewModal.show();

        $('#btnConfirmSubmit').on('click', function () {
            submitStockAdjustment();
        });
    }

    // Open modal for add/edit
    function openItemModal(index)
    {
        if (typeof index === 'undefined') index = -1;

        if (index >= 0) {
            $('#itemModalLabel').html('<i class="bi bi-pencil-square me-2"></i>Edit Item');
        } else {
            $('#itemModalLabel').html('<i class="bi bi-plus-circle me-2"></i>Add Item');
        }

        $('#itemForm')[ 0 ].reset();
        $('#editIndex').val(index);
        $('#modalItemDesc').val('');
        $('#modalCost').val('');
        $('#modalStockOnHand').val('');

        // Show modal first so Select2 can measure properly
        var myModal = new bootstrap.Modal(document.getElementById('itemModal'));
        myModal.show();

        // Initialize Select2 after modal is fully shown
        $('#itemModal').off('shown.bs.modal.s2init').on('shown.bs.modal.s2init', function ()
        {
            initSelect2OnModal();

            if (index >= 0) {
                // Edit mode - populate form
                var item = gridData[ index ];
                $('#stockRecSno').val(item.stockRecSno);
                $('#modalRecType').val(item.recType).trigger('change');
                $('#modalItemCode').val(item.itemCode).trigger('change');
                $('#modalLocation').val(item.location).trigger('change');
                $('#modalQty').val(item.qty);
                $('#modalItemDesc').val(item.itemDescription);
                $('#modalCost').val(item.cost ? parseFloat(item.cost).toFixed(4) : '');
                $('#modalStockOnHand').val(item.stockOnHand != null ? parseFloat(item.stockOnHand).toFixed(3) : '');
            } else {
                // Add mode
                $('#stockRecSno').val(nextStockRecSno);
                $('#modalRecType').val('').trigger('change');
                $('#modalItemCode').val('').trigger('change');
                $('#modalLocation').val('').trigger('change');
            }
        });
    }

    // Load item description from cached items
    function loadItemDescription(itemCode)
    {
        if (!itemCode) {
            $('#modalItemDesc').val('');
            $('#modalCost').val('');
            return;
        }

        var found = cachedItems.find(function (i) { return i.id === itemCode; });
        if (found) {
            var parts = found.text.split(' - ');
            var desc = parts.length > 1 ? parts.slice(1).join(' - ') : found.text;
            $('#modalItemDesc').val(desc);
        } else
        {
            $.ajax({
                url: appBasePath + '/StockAdjustment/GetItemDetails',
                type: 'POST',
                data: { itemCode: itemCode },
                success: function (response)
                {
                    if (response.success) {
                        $('#modalItemDesc').val(response.description);
                    }
                }
            });
        }

        // Always fetch cost from Sage ItemSearch API
        loadItemCost(itemCode);
    }

    // Fetch cost (stdcost) from Sage ItemSearch API endpoint
    function loadItemCost(itemCode)
    {
        $('#modalCost').val('Loading...');
        $.ajax({
            url: appBasePath + '/StockAdjustment/GetItemCost',
            type: 'POST',
            data: { itemCode: itemCode },
            success: function (response)
            {
                if (response.success) {
                    $('#modalCost').val(response.cost != null ? parseFloat(response.cost).toFixed(4) : '0.0000');
                } else {
                    $('#modalCost').val('');
                }
            },
            error: function () {
                $('#modalCost').val('');
                showAlert('Error fetching item cost from Sage', 'error');
            }
        });
    }

    // Fetch stock quantity on hand from Sage GetICStock API
    function loadStockQty()
    {
        var itemCode = $('#modalItemCode').val();
        var location = $('#modalLocation').val();

        if (!itemCode || !location) {
            $('#modalStockOnHand').val('');
            return;
        }

        $('#modalStockOnHand').val('Loading...');
        $.ajax({
            url: appBasePath + '/StockAdjustment/GetStockQty',
            type: 'POST',
            data: { itemCode: itemCode, location: location },
            success: function (response)
            {
                if (response.success) {
                    $('#modalStockOnHand').val(response.qtonhand != null ? parseFloat(response.qtonhand).toFixed(3) : '0.000');
                } else {
                    $('#modalStockOnHand').val('');
                }
            },
            error: function () {
                $('#modalStockOnHand').val('');
                showAlert('Error fetching stock quantity from Sage', 'error');
            }
        });
    }

    // Save item to grid
    function saveItem()
    {
        // Validate form
        if (!$('#itemForm')[ 0 ].checkValidity()) {
            $('#itemForm')[ 0 ].reportValidity();
            return;
        }

        var editIndex = parseInt($('#editIndex').val());
        var stockRecSno = parseInt($('#stockRecSno').val());
        var recType = parseInt($('#modalRecType').val());
        var recTypeName = $('#modalRecType option:selected').text();
        var itemCode = $('#modalItemCode').val();
        var itemDescription = $('#modalItemDesc').val();
        var location = $('#modalLocation').val();
        var locationName = getLocationDisplayText(location);
        var qty = parseFloat($('#modalQty').val());
        var cost = $('#modalCost').val();
        var stockOnHand = $('#modalStockOnHand').val();

        if (!recType) {
            showAlert('Please select an Adjustment Type', 'warning');
            return;
        }
        if (!itemCode) {
            showAlert('Please select an Item', 'warning');
            return;
        }
        if (!location) {
            showAlert('Please select a Location', 'warning');
            return;
        }

        // Derive From/To Location based on Adjustment Type
        var fromLocation, toLocation, fromLocationName, toLocationName;
        if (recType === 12) {
            // Stock Increase: both From and To = selected Location
            fromLocation = location;
            toLocation = location;
            fromLocationName = locationName;
            toLocationName = locationName;
        } else if (recType === 10) {
            // Stock Decrease: From = selected Location, To = default from App Options
            fromLocation = location;
            toLocation = defaultAppLocation;
            fromLocationName = locationName;
            toLocationName = getLocationDisplayText(defaultAppLocation) || defaultAppLocation;
        } else {
            fromLocation = location;
            toLocation = location;
            fromLocationName = locationName;
            toLocationName = locationName;
        }

        var item = {
            stockRecSno: stockRecSno,
            recType: recType,
            recTypeName: recTypeName,
            itemCode: itemCode,
            itemDescription: itemDescription,
            location: location,
            locationName: locationName,
            fromLocation: fromLocation,
            fromLocationName: fromLocationName,
            toLocation: toLocation,
            toLocationName: toLocationName,
            qty: qty,
            cost: cost,
            stockOnHand: stockOnHand
        };

        if (editIndex >= 0) {
            gridData[ editIndex ] = item;
        } else {
            gridData.push(item);
            nextStockRecSno++;
        }

        refreshGrid();

        var modalElement = document.getElementById('itemModal');
        var modal = bootstrap.Modal.getInstance(modalElement);
        modal.hide();

        showAlert('Item ' + (editIndex >= 0 ? 'updated' : 'added') + ' successfully', 'success');
    }

    // Refresh grid display
    function refreshGrid()
    {
        var tbody = $('#itemsTableBody');
        tbody.empty();

        if (gridData.length === 0) {
            tbody.append('<tr id="noDataRow"><td colspan="9" class="text-center text-muted">No items added yet. Click "Add Item" to begin.</td></tr>');
            updateRateSummary();
            return;
        }

        gridData.forEach(function (item, index) {
            var displaySno = index + 1;
            var costDisplay = item.cost ? parseFloat(item.cost).toFixed(4) : '0.0000';
            var stockOnHandDisplay = item.stockOnHand ? parseFloat(item.stockOnHand).toFixed(3) : '0.000';
            var row = '<tr>' +
                '<td>' + displaySno + '</td>' +
                '<td>' + escapeHtml(item.recTypeName) + '</td>' +
                '<td>' + escapeHtml(item.itemCode) + '</td>' +
                '<td>' + escapeHtml(item.itemDescription) + '</td>' +
                '<td>' + escapeHtml(item.locationName) + '</td>' +
                '<td>' + stockOnHandDisplay + '</td>' +
                '<td>' + item.qty.toFixed(3) + '</td>' +
                '<td>' + costDisplay + '</td>' +
                '<td>' +
                '<button type="button" class="btn btn-sm btn-warning me-1" onclick="editItem(' + index + ')">' +
                '<i class="bi bi-pencil-square"></i> Edit</button>' +
                '<button type="button" class="btn btn-sm btn-danger" onclick="deleteItem(' + index + ')">' +
                '<i class="bi bi-trash"></i> Delete</button>' +
                '</td></tr>';
            tbody.append(row);
        });

        updateRateSummary();
    }

    // Update Stock Increase Rate and Stock Decrease Rate summary
    function updateRateSummary()
    {
        var increaseTotal = 0;
        var decreaseTotal = 0;

        gridData.forEach(function (item)
        {
            var cost = item.cost ? parseFloat(item.cost) : 0;
            var qty = item.qty ? parseFloat(item.qty) : 0;
            var amount = cost * qty;

            if (item.recType === 12) {
                increaseTotal += amount;
            } else if (item.recType === 10) {
                decreaseTotal += amount;
            }
        });

        $('#stockIncreaseRate').val(increaseTotal.toFixed(4));
        $('#stockDecreaseRate').val(decreaseTotal.toFixed(4));
    }

    // Edit item
    window.editItem = function (index) {
        openItemModal(index);
    };

    // Delete item
    window.deleteItem = function (index)
    {
        if (confirm('Are you sure you want to delete this item?')) {
            gridData.splice(index, 1);
            refreshGrid();
            showAlert('Item deleted successfully', 'success');
        }
    };

    // Submit stock adjustment (called from review modal)
    function submitStockAdjustment()
    {
        var formData = new FormData();

        formData.append('transactionDate', $('#requestDate').val());

        formData.append('lineItemsJson', JSON.stringify(gridData.map(function (item)
        {
            return {
                stockRecSno: item.stockRecSno,
                recType: item.recType,
                fromLocation: item.fromLocation,
                toLocation: item.toLocation,
                itemCode: item.itemCode,
                itemDescription: item.itemDescription,
                qty: item.qty,
                cost: item.cost ? parseFloat(item.cost) : 0
            };
        })));

        for (var fileTypeId in uploadedFiles) {
            formData.append('file_' + fileTypeId, uploadedFiles[ fileTypeId ]);
        }

        showSpinner('Submitting stock adjustment...');

        $.ajax({
            url: appBasePath + '/StockAdjustment/SaveStockAdjustment',
            type: 'POST',
            data: formData,
            processData: false,
            contentType: false,
            beforeSend: function () {
                $('#btnConfirmSubmit').prop('disabled', true).html('<i class="bi bi-hourglass-split me-2"></i> Saving...');
            },
            success: function (response)
            {
                hideSpinner();

                if (response.success)
                {
                    var reviewModalEl = document.getElementById('reviewModal');
                    var reviewModal = bootstrap.Modal.getInstance(reviewModalEl);
                    reviewModal.hide();

                    // Show success popup that user must acknowledge
                    showSuccessPopup(response.message || 'Stock adjustment saved successfully!');
                } else
                {
                    showErrorPopup(response.message || 'Failed to save stock adjustment', response.sageResults);
                    $('#btnConfirmSubmit').prop('disabled', false).html('<i class="bi bi-check-circle me-2"></i>Confirm & Submit');
                }
            },
            error: function () {
                hideSpinner();
                showErrorPopup('An error occurred while saving');
                $('#btnConfirmSubmit').prop('disabled', false).html('<i class="bi bi-check-circle me-2"></i>Confirm & Submit');
            }
        });
    }

    // Show success popup modal — user must click OK, then page reloads
    function showSuccessPopup(message)
    {
        $('#resultPopupModal').remove();
        var html = '<div class="modal fade" id="resultPopupModal" tabindex="-1" data-bs-backdrop="static" data-bs-keyboard="false">' +
            '<div class="modal-dialog modal-dialog-centered"><div class="modal-content border-0 shadow">' +
            '<div class="modal-body text-center p-5">' +
            '<div class="mb-3"><i class="bi bi-check-circle-fill text-success" style="font-size:4rem;"></i></div>' +
            '<h4 class="fw-bold text-success mb-3">Success!</h4>' +
            '<p class="fs-5 mb-0">' + escapeHtml(message) + '</p>' +
            '</div>' +
            '<div class="modal-footer justify-content-center border-0 pb-4">' +
            '<button type="button" class="btn btn-success px-5 btn-lg" id="btnResultOk"><i class="bi bi-check-lg me-2"></i>OK</button>' +
            '</div></div></div></div>';
        $('body').append(html);
        var modal = new bootstrap.Modal(document.getElementById('resultPopupModal'));
        modal.show();
        $('#btnResultOk').on('click', function () {
            modal.hide();
            window.location.reload();
        });
    }

    // Show error popup modal with optional Sage API response details
    function showErrorPopup(message, sageResults)
    {
        $('#resultPopupModal').remove();

        var sageDetailsHtml = '';
        if (sageResults && sageResults.length > 0) {
            sageDetailsHtml += '<div class="text-start mt-4">';
            sageResults.forEach(function (sage, index) {
                var isSuccess = sage.isSuccess === true;
                var statusClass = isSuccess ? 'success' : 'danger';
                var statusIcon = isSuccess ? 'bi-check-circle-fill' : 'bi-x-circle-fill';
                var docRef = sage.documentReference || ('RecType ' + sage.recType);

                sageDetailsHtml += '<div class="card mb-2 border-' + statusClass + '">' +
                    '<div class="card-header bg-' + statusClass + ' bg-opacity-10 py-2 d-flex justify-content-between align-items-center">' +
                    '<span><i class="bi ' + statusIcon + ' text-' + statusClass + ' me-1"></i><strong>' + escapeHtml(docRef) + '</strong></span>' +
                    '<span class="badge bg-' + statusClass + '">' + escapeHtml(String(sage.sageStatus || 'Unknown')) + '</span></div>' +
                    '<div class="card-body py-2"><small><strong>Message:</strong> ' + escapeHtml(sage.sageMessage || 'No message') + '</small>';

                if (sage.sageRawRequest || sage.sageRawResponse) {
                    sageDetailsHtml += '<div class="mt-2">';
                    if (sage.sageRawRequest) {
                        sageDetailsHtml += '<button class="btn btn-sm btn-outline-primary me-1" type="button" data-bs-toggle="collapse" data-bs-target="#errReq' + index + '"><i class="bi bi-arrow-up-circle me-1"></i>Request</button>';
                    }
                    if (sage.sageRawResponse) {
                        sageDetailsHtml += '<button class="btn btn-sm btn-outline-secondary" type="button" data-bs-toggle="collapse" data-bs-target="#errRes' + index + '"><i class="bi bi-arrow-down-circle me-1"></i>Response</button>';
                    }
                    if (sage.sageRawRequest) {
                        sageDetailsHtml += '<div class="collapse mt-2" id="errReq' + index + '"><pre class="bg-dark text-light p-2 rounded" style="max-height:200px;overflow-y:auto;font-size:0.8rem;white-space:pre-wrap;">' + escapeHtml(formatJson(sage.sageRawRequest) || 'No request data') + '</pre></div>';
                    }
                    if (sage.sageRawResponse) {
                        sageDetailsHtml += '<div class="collapse mt-2" id="errRes' + index + '"><pre class="bg-dark text-light p-2 rounded" style="max-height:200px;overflow-y:auto;font-size:0.8rem;white-space:pre-wrap;">' + escapeHtml(formatJson(sage.sageRawResponse) || 'No response data') + '</pre></div>';
                    }
                    sageDetailsHtml += '</div>';
                }

                sageDetailsHtml += '</div></div>';
            });
            sageDetailsHtml += '</div>';
        }

        var html = '<div class="modal fade" id="resultPopupModal" tabindex="-1" data-bs-backdrop="static" data-bs-keyboard="false">' +
            '<div class="modal-dialog modal-dialog-centered' + (sageResults && sageResults.length > 0 ? ' modal-lg' : '') + '"><div class="modal-content border-0 shadow">' +
            '<div class="modal-body text-center p-4">' +
            '<div class="mb-3"><i class="bi bi-x-circle-fill text-danger" style="font-size:4rem;"></i></div>' +
            '<h4 class="fw-bold text-danger mb-3">Transaction Failed</h4>' +
            '<p class="mb-1">' + escapeHtml(message) + '</p>' +
            '<p class="text-muted small">All records for this transaction have been rolled back.</p>' +
            sageDetailsHtml +
            '</div>' +
            '<div class="modal-footer justify-content-center border-0 pb-4">' +
            '<button type="button" class="btn btn-danger px-5 btn-lg" data-bs-dismiss="modal"><i class="bi bi-x-lg me-2"></i>Close</button>' +
            '</div></div></div></div>';
        $('body').append(html);
        var modal = new bootstrap.Modal(document.getElementById('resultPopupModal'));
        modal.show();
    }

    // Show toast notification
    function showAlert(message, type)
    {
        var toastEl, toastMessage;

        if (type === 'success') {
            toastEl = document.getElementById('successToast');
            toastMessage = document.getElementById('successToastMessage');
        } else if (type === 'warning') {
            toastEl = document.getElementById('warningToast');
            toastMessage = document.getElementById('warningToastMessage');
        } else {
            toastEl = document.getElementById('errorToast');
            toastMessage = document.getElementById('errorToastMessage');
        }

        toastMessage.textContent = message;

        var toastInstance = new bootstrap.Toast(toastEl, {
            delay: type === 'success' ? 3000 : type === 'warning' ? 4000 : 5000
        });
        toastInstance.show();
    }

    // Show Sage API response in a modal
    function showSageResponseModal(dbMessage, sageResults)
    {
        var hasErrors = false;
        var sageHtml = '';
        sageResults.forEach(function (sage, index)
        {
            var isSuccess = sage.isSuccess === true;
            var statusClass = isSuccess ? 'success' : 'danger';
            var statusIcon = isSuccess ? 'bi-check-circle-fill' : 'bi-x-circle-fill';
            var docRef = sage.documentReference || ('RecType ' + sage.recType + ' | RecNumber ' + sage.recNumber);

            if (!isSuccess) hasErrors = true;

            sageHtml += '<div class="card mb-3 border-' + statusClass + ' border-2">' +
                '<div class="card-header bg-' + statusClass + ' bg-opacity-10 d-flex justify-content-between align-items-center">' +
                '<span><i class="bi ' + statusIcon + ' text-' + statusClass + ' me-2"></i><strong>' + escapeHtml(docRef) + '</strong></span>' +
                '<span class="badge bg-' + statusClass + '">' + escapeHtml(String(sage.sageStatus || 'Unknown')) + '</span></div>' +
                '<div class="card-body"><p class="mb-2"><strong>Message:</strong> ' + escapeHtml(sage.sageMessage || 'No message') + '</p>';

            sageHtml += '<div class="mt-2">' +
                '<button class="btn btn-sm btn-outline-primary me-2" type="button" data-bs-toggle="collapse" data-bs-target="#sageReq' + index + '"><i class="bi bi-arrow-up-circle me-1"></i>View Request</button>' +
                '<button class="btn btn-sm btn-outline-secondary" type="button" data-bs-toggle="collapse" data-bs-target="#sageRes' + index + '"><i class="bi bi-arrow-down-circle me-1"></i>View Response</button>' +
                '<div class="collapse mt-2" id="sageReq' + index + '"><pre class="bg-dark text-light p-3 rounded" style="max-height:300px;overflow-y:auto;font-size:0.85rem;white-space:pre-wrap;">' + escapeHtml(formatJson(sage.sageRawRequest) || 'No request data') + '</pre></div>' +
                '<div class="collapse mt-2" id="sageRes' + index + '"><pre class="bg-dark text-light p-3 rounded" style="max-height:300px;overflow-y:auto;font-size:0.85rem;white-space:pre-wrap;">' + escapeHtml(formatJson(sage.sageRawResponse) || 'No response data') + '</pre></div>' +
                '</div>';

            sageHtml += '</div></div>';
        });

        var alertClass = hasErrors ? 'alert-danger' : 'alert-success';
        var alertIcon = hasErrors ? 'bi-exclamation-triangle-fill' : 'bi-database-check';

        var modalHtml = '<div class="modal fade" id="sageResponseModal" tabindex="-1" data-bs-backdrop="static" data-bs-keyboard="false">' +
            '<div class="modal-dialog modal-lg"><div class="modal-content">' +
            '<div class="modal-header bg-info text-white"><h5 class="modal-title"><i class="bi bi-cloud-arrow-up me-2"></i>Transaction Results</h5></div>' +
            '<div class="modal-body p-4">' +
            '<div class="alert ' + alertClass + ' mb-4"><i class="bi ' + alertIcon + ' me-2"></i>' + escapeHtml(dbMessage) + '</div>' +
            '<h6 class="fw-bold mb-3">Results (' + sageResults.length + ')</h6>' + sageHtml +
            '</div><div class="modal-footer"><button type="button" class="btn btn-primary" id="btnCloseSageModal"><i class="bi bi-check-lg me-2"></i>OK</button></div>' +
            '</div></div></div>';

        $('#sageResponseModal').remove();
        $('body').append(modalHtml);

        var sageModal = new bootstrap.Modal(document.getElementById('sageResponseModal'));
        sageModal.show();

        $('#btnCloseSageModal').on('click', function ()
        {
            sageModal.hide();
            setTimeout(function () { window.location.reload(); }, 500);
        });
    }

    // Format JSON for display
    function formatJson(str)
    {
        if (!str) return '';
        try {
            return JSON.stringify(JSON.parse(str), null, 2);
        } catch (e) {
            return str;
        }
    }

    // ==================== EXCEL IMPORT ====================

    function importExcelFile(file)
    {
        var formData = new FormData();
        formData.append('file', file);

        showSpinner('Importing and validating Excel file...');

        $.ajax({
            url: appBasePath + '/StockAdjustment/ImportExcel',
            type: 'POST',
            data: formData,
            processData: false,
            contentType: false,
            success: function (response)
            {
                hideSpinner();

                if (response.success)
                {
                    var imported = response.data;
                    var snoStart = nextStockRecSno;
                    for (var i = 0; i < imported.length; i++)
                    {
                        var row = imported[ i ];
                        gridData.push({
                            stockRecSno: snoStart + i,
                            recType: row.recType,
                            recTypeName: row.recTypeName,
                            itemCode: row.itemCode,
                            itemDescription: row.itemDescription,
                            location: row.location,
                            locationName: row.locationName,
                            fromLocation: row.fromLocation,
                            fromLocationName: row.fromLocationName,
                            toLocation: row.toLocation,
                            toLocationName: row.toLocationName,
                            qty: row.qty,
                            cost: row.cost || '0'
                        });
                    }
                    nextStockRecSno = snoStart + imported.length;
                    refreshGrid();
                    showAlert(response.message, 'success');
                } else if (response.errors && response.errors.length > 0) {
                    showImportErrorsModal(response.errors);
                } else {
                    showAlert(response.message || 'Import failed', 'error');
                }
            },
            error: function () {
                hideSpinner();
                showAlert('Error uploading Excel file', 'error');
            }
        });
    }

    function showImportErrorsModal(errors)
    {
        var errorsHtml = '';
        errors.forEach(function (err, index) {
            errorsHtml += '<tr>' +
                '<td class="text-center">' + (index + 1) + '</td>' +
                '<td class="text-center"><span class="badge bg-secondary">' + escapeHtml(err.cell) + '</span></td>' +
                '<td>' + escapeHtml(err.field) + '</td>' +
                '<td class="text-danger">' + escapeHtml(err.message) + '</td>' +
                '</tr>';
        });

        var modalHtml = '<div class="modal fade" id="importErrorsModal" tabindex="-1" data-bs-backdrop="static" data-bs-keyboard="false">' +
            '<div class="modal-dialog modal-lg modal-dialog-scrollable"><div class="modal-content">' +
            '<div class="modal-header bg-danger text-white">' +
            '<h5 class="modal-title"><i class="bi bi-exclamation-triangle-fill me-2"></i>Import Errors (' + errors.length + ')</h5>' +
            '<button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal" aria-label="Close"></button></div>' +
            '<div class="modal-body p-4">' +
            '<div class="alert alert-warning mb-3"><i class="bi bi-info-circle-fill me-2"></i>The following errors were found. Please fix them and re-import.</div>' +
            '<div class="table-responsive"><table class="table table-bordered table-sm table-hover">' +
            '<thead class="table-light"><tr><th style="width:5%;">#</th><th style="width:10%;">Cell</th><th style="width:20%;">Field</th><th>Error</th></tr></thead>' +
            '<tbody>' + errorsHtml + '</tbody></table></div></div>' +
            '<div class="modal-footer"><button type="button" class="btn btn-secondary" data-bs-dismiss="modal"><i class="bi bi-x-circle me-1"></i>Close</button></div>' +
            '</div></div></div>';

        $('#importErrorsModal').remove();
        $('body').append(modalHtml);

        var errModal = new bootstrap.Modal(document.getElementById('importErrorsModal'));
        errModal.show();
    }

    // Escape HTML to prevent XSS
    function escapeHtml(text) {
        if (!text) return '';
        var div = document.createElement('div');
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }

})();
