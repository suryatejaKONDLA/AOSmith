// Stock Adjustment Request - JavaScript
(function () {
    'use strict';

    // Global variables
    let gridData = [];
    let nextStockRecSno = 1;
    let uploadedFiles = {};

    // Initialize on document ready
    $(document).ready(function () {
        initializeEvents();
    });

    function initializeEvents() {
        // Add Item button
        $('#btnAddItem').on('click', function () {
            openItemModal();
        });

        // Save Item button in modal
        $('#btnSaveItem').on('click', function () {
            saveItem();
        });

        // Item Code selection - auto-fill description
        $('#modalItemCode').on('change', function () {
            loadItemDescription($(this).val());
        });

        // REC Type change - validate locations
        $('#modalRecType').on('change', function () {
            handleRecTypeChange();
        });

        // Location changes - validate based on REC Type
        $('#modalFromLocation, #modalToLocation').on('change', function () {
            validateLocations();
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
    }

    // Handle file upload
    function handleFileUpload($input) {
        const fileTypeId = $input.data('file-type');
        const maxSize = $input.data('max-size'); // in KB
        const file = $input[0].files[0];

        if (!file) {
            return;
        }

        // Validate file size
        const fileSizeKB = file.size / 1024;
        if (fileSizeKB > maxSize) {
            showAlert(`File size exceeds maximum limit of ${maxSize / 1024} MB`, 'warning');
            $input.val('');
            return;
        }

        // Store file
        uploadedFiles[fileTypeId] = file;

        // Show file name
        $('#fileName_' + fileTypeId).text(file.name + ' (' + (fileSizeKB / 1024).toFixed(2) + ' MB)');

        // Show clear button
        $('.btn-clear-file[data-file-id="' + fileTypeId + '"]').show();
    }

    // Clear file
    function clearFile(fileTypeId) {
        delete uploadedFiles[fileTypeId];
        $('#file_' + fileTypeId).val('');
        $('#fileName_' + fileTypeId).text('');
        $('.btn-clear-file[data-file-id="' + fileTypeId + '"]').hide();
    }

    // Review Data
    function reviewData() {
        // Validate grid data
        if (gridData.length === 0) {
            showAlert('Please add at least one item', 'warning');
            return;
        }

        // Validate required files
        let missingFiles = [];
        $('.file-upload').each(function () {
            const $input = $(this);
            const isRequired = $input.data('required') === true || $input.data('required') === 'true';
            const fileTypeId = $input.data('file-type');
            const label = $input.closest('.mb-3').find('label').text().trim();

            if (isRequired && !uploadedFiles[fileTypeId]) {
                missingFiles.push(label.split('*')[0].trim());
            }
        });

        if (missingFiles.length > 0) {
            showAlert('Please upload required files: ' + missingFiles.join(', '), 'warning');
            return;
        }

        // Show review modal or navigate to review page
        showReviewModal();
    }

    // Show Review Modal
    function showReviewModal() {
        // Build review data HTML
        let itemsHtml = '';
        gridData.forEach(function (item, index) {
            itemsHtml += `
                <tr>
                    <td>${index + 1}</td>
                    <td>${item.itemCode}</td>
                    <td>${item.itemDescription}</td>
                    <td>${item.fromLocationName}</td>
                    <td>${item.toLocationName}</td>
                    <td>${item.recTypeName}</td>
                    <td>${item.qty.toFixed(3)}</td>
                </tr>
            `;
        });

        let filesHtml = '';
        for (let fileTypeId in uploadedFiles) {
            const file = uploadedFiles[fileTypeId];
            filesHtml += `<li>${file.name} (${(file.size / 1024 / 1024).toFixed(2)} MB)</li>`;
        }

        const reviewHtml = `
            <div class="modal fade" id="reviewModal" tabindex="-1" data-bs-backdrop="static" data-bs-keyboard="false">
                <div class="modal-dialog modal-xl">
                    <div class="modal-content">
                        <div class="modal-header bg-primary text-white">
                            <h5 class="modal-title"><i class="bi bi-eye-fill me-2"></i>Review Stock Adjustment</h5>
                        </div>
                        <div class="modal-body p-4">
                            <h6 class="fw-bold mb-3">Transaction Details</h6>
                            <div class="row mb-4">
                                <div class="col-md-6">
                                    <p><strong>Request Date:</strong> ${$('#requestDate').val()}</p>
                                </div>
                                <div class="col-md-6">
                                    <p><strong>Requestor:</strong> ${$('#requestor').val()}</p>
                                </div>
                            </div>

                            <h6 class="fw-bold mb-3">Items (${gridData.length})</h6>
                            <div class="table-responsive mb-4">
                                <table class="table table-bordered table-sm">
                                    <thead class="table-light">
                                        <tr>
                                            <th>Sr No</th>
                                            <th>Item Code</th>
                                            <th>Description</th>
                                            <th>From Location</th>
                                            <th>To Location</th>
                                            <th>Type</th>
                                            <th>Qty</th>
                                        </tr>
                                    </thead>
                                    <tbody>${itemsHtml}</tbody>
                                </table>
                            </div>

                            <h6 class="fw-bold mb-3">Attachments (${Object.keys(uploadedFiles).length})</h6>
                            <ul class="list-unstyled">${filesHtml}</ul>
                        </div>
                        <div class="modal-footer">
                            <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">
                                <i class="bi bi-arrow-left me-2"></i>Back to Edit
                            </button>
                            <button type="button" class="btn btn-success" id="btnConfirmSubmit">
                                <i class="bi bi-check-circle me-2"></i>Confirm & Submit
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        `;

        // Remove existing review modal if any
        $('#reviewModal').remove();

        // Add review modal to body
        $('body').append(reviewHtml);

        // Show modal
        var reviewModal = new bootstrap.Modal(document.getElementById('reviewModal'));
        reviewModal.show();

        // Handle confirm submit
        $('#btnConfirmSubmit').on('click', function () {
            submitStockAdjustment();
        });
    }

    // Open modal for add/edit
    function openItemModal(index = -1) {
        if (index >= 0) {
            $('#itemModalLabel').html('<i class="bi bi-pencil-square me-2"></i>Edit Item');
        } else {
            $('#itemModalLabel').html('<i class="bi bi-plus-circle me-2"></i>Add Item');
        }

        $('#itemForm')[0].reset();
        $('#editIndex').val(index);
        $('#modalItemDesc').val('');
        $('#locationMatchWarning').hide();

        // Reset To Location to enabled state
        $('#modalToLocation').prop('disabled', false).css('background-color', '');

        if (index >= 0) {
            // Edit mode - populate form
            const item = gridData[index];
            $('#stockRecSno').val(item.stockRecSno);
            $('#modalRecType').val(item.recType);
            $('#modalItemCode').val(item.itemCode);
            $('#modalFromLocation').val(item.fromLocation);
            $('#modalToLocation').val(item.toLocation);
            $('#modalQty').val(item.qty);
            $('#modalItemDesc').val(item.itemDescription);
            handleRecTypeChange();
        } else {
            // Add mode - assign next SNO
            $('#stockRecSno').val(nextStockRecSno);
        }

        // Bootstrap 5 way to show modal
        var myModal = new bootstrap.Modal(document.getElementById('itemModal'));
        myModal.show();
    }

    // Load item description
    function loadItemDescription(itemCode) {
        if (!itemCode) {
            $('#modalItemDesc').val('');
            return;
        }

        $.ajax({
            url: '/StockAdjustment/GetItemDetails',
            type: 'POST',
            data: { itemCode: itemCode },
            success: function (response) {
                if (response.success) {
                    $('#modalItemDesc').val(response.description);
                }
            },
            error: function () {
                showAlert('Error loading item details', 'error');
            }
        });
    }

    // Handle REC Type change
    function handleRecTypeChange() {
        const recType = parseInt($('#modalRecType').val());

        if (recType === 12) {
            // REC Type 12 (STOCK INCREASE): From and To must match
            $('#locationMatchWarning').show();

            // Clear and enable To Location
            $('#modalToLocation').val('');
            $('#modalToLocation').prop('disabled', false).css('background-color', '');

            // Auto-sync To location when From changes
            $('#modalFromLocation').off('change.sync').on('change.sync', function () {
                $('#modalToLocation').val($(this).val());
            });

            // Auto-sync From location when To changes
            $('#modalToLocation').off('change.sync').on('change.sync', function () {
                $('#modalFromLocation').val($(this).val());
            });
        } else if (recType === 10) {
            // REC Type 10 (STOCK DECREASE): To Location is read-only from APP_Options
            $('#locationMatchWarning').hide();
            $('#modalFromLocation').off('change.sync');
            $('#modalToLocation').off('change.sync');

            // Load default location from APP_Options and set To Location as readonly
            loadDefaultLocationForStockDecrease();
        } else {
            // Other types or no selection
            $('#locationMatchWarning').hide();
            $('#modalFromLocation').off('change.sync');
            $('#modalToLocation').off('change.sync');

            // Clear and enable To Location
            $('#modalToLocation').val('');
            $('#modalToLocation').prop('disabled', false).css('background-color', '');
        }
    }

    // Load default location for Stock Decrease (REC Type 10)
    function loadDefaultLocationForStockDecrease() {
        $.ajax({
            url: '/StockAdjustment/GetDefaultLocation',
            type: 'POST',
            success: function (response) {
                if (response.success) {
                    $('#modalToLocation').val(response.location);
                    $('#modalToLocation').prop('disabled', true).css('background-color', '#e9ecef');
                } else {
                    showAlert(response.message || 'Default location not configured', 'error');
                    $('#modalToLocation').val('');
                    $('#modalToLocation').prop('disabled', true).css('background-color', '#e9ecef');
                }
            },
            error: function () {
                showAlert('Error loading default location', 'error');
                $('#modalToLocation').prop('disabled', true).css('background-color', '#e9ecef');
            }
        });
    }

    // Validate locations based on REC Type
    function validateLocations() {
        const recType = parseInt($('#modalRecType').val());
        const fromLoc = $('#modalFromLocation').val();
        const toLoc = $('#modalToLocation').val();

        if (recType === 12 && fromLoc && toLoc && fromLoc !== toLoc) {
            showAlert('For Stock Increase, From and To locations must be the same', 'warning');
            return false;
        }

        return true;
    }

    // Save item to grid
    function saveItem() {
        // Validate form
        if (!$('#itemForm')[0].checkValidity()) {
            $('#itemForm')[0].reportValidity();
            return;
        }

        // Validate locations
        if (!validateLocations()) {
            return;
        }

        const editIndex = parseInt($('#editIndex').val());
        const stockRecSno = parseInt($('#stockRecSno').val());
        const recType = parseInt($('#modalRecType').val());
        const recTypeName = $('#modalRecType option:selected').text();
        const itemCode = $('#modalItemCode').val();
        const itemDescription = $('#modalItemDesc').val();
        const fromLocation = $('#modalFromLocation').val();
        const fromLocationName = $('#modalFromLocation option:selected').text();
        const toLocation = $('#modalToLocation').val();
        const toLocationName = $('#modalToLocation option:selected').text();
        const qty = parseFloat($('#modalQty').val());

        const item = {
            stockRecSno: stockRecSno,
            recType: recType,
            recTypeName: recTypeName,
            itemCode: itemCode,
            itemDescription: itemDescription,
            fromLocation: fromLocation,
            fromLocationName: fromLocationName,
            toLocation: toLocation,
            toLocationName: toLocationName,
            qty: qty
        };

        if (editIndex >= 0) {
            // Update existing item
            gridData[editIndex] = item;
        } else {
            // Add new item
            gridData.push(item);
            nextStockRecSno++;
        }

        refreshGrid();

        // Bootstrap 5 way to hide modal
        var modalElement = document.getElementById('itemModal');
        var modal = bootstrap.Modal.getInstance(modalElement);
        modal.hide();

        showAlert('Item ' + (editIndex >= 0 ? 'updated' : 'added') + ' successfully', 'success');
    }

    // Refresh grid display
    function refreshGrid() {
        const tbody = $('#itemsTableBody');
        tbody.empty();

        if (gridData.length === 0) {
            tbody.append('<tr id="noDataRow"><td colspan="8" class="text-center text-muted">No items added yet. Click "Add Item" to begin.</td></tr>');
            return;
        }

        gridData.forEach(function (item, index) {
            const displaySno = index + 1; // Always 1, 2, 3, 4...
            const row = `
                <tr>
                    <td>${displaySno}</td>
                    <td>${item.itemCode}</td>
                    <td>${item.itemDescription}</td>
                    <td>${item.fromLocationName}</td>
                    <td>${item.toLocationName}</td>
                    <td>${item.recTypeName}</td>
                    <td>${item.qty.toFixed(3)}</td>
                    <td>
                        <button type="button" class="btn btn-sm btn-warning" onclick="editItem(${index})">
                            <i class="fas fa-edit"></i> Edit
                        </button>
                        <button type="button" class="btn btn-sm btn-danger" onclick="deleteItem(${index})">
                            <i class="fas fa-trash"></i> Delete
                        </button>
                    </td>
                </tr>
            `;
            tbody.append(row);
        });
    }

    // Edit item
    window.editItem = function (index) {
        openItemModal(index);
    };

    // Delete item
    window.deleteItem = function (index) {
        if (confirm('Are you sure you want to delete this item?')) {
            gridData.splice(index, 1);
            refreshGrid();
            showAlert('Item deleted successfully', 'success');
        }
    };

    // Submit stock adjustment (called from review modal)
    function submitStockAdjustment() {
        // Prepare FormData for file upload
        const formData = new FormData();

        // Add transaction data
        formData.append('transactionDate', $('#requestDate').val());

        // Add line items as JSON
        formData.append('lineItemsJson', JSON.stringify(gridData.map(function (item) {
            return {
                stockRecSno: item.stockRecSno,
                recType: item.recType,
                fromLocation: item.fromLocation,
                toLocation: item.toLocation,
                itemCode: item.itemCode,
                itemDescription: item.itemDescription,
                qty: item.qty
            };
        })));

        // Add files
        for (let fileTypeId in uploadedFiles) {
            formData.append('file_' + fileTypeId, uploadedFiles[fileTypeId]);
        }

        $.ajax({
            url: '/StockAdjustment/SaveStockAdjustment',
            type: 'POST',
            data: formData,
            processData: false,
            contentType: false,
            beforeSend: function () {
                $('#btnConfirmSubmit').prop('disabled', true).html('<i class="bi bi-hourglass-split me-2"></i> Saving...');
            },
            success: function (response) {
                if (response.success) {
                    // Close review modal
                    var reviewModalEl = document.getElementById('reviewModal');
                    var reviewModal = bootstrap.Modal.getInstance(reviewModalEl);
                    reviewModal.hide();

                    showAlert('Stock adjustment saved successfully!', 'success');

                    // Reset form after 2 seconds
                    setTimeout(function () {
                        window.location.reload();
                    }, 2000);
                } else {
                    showAlert(response.message || 'Failed to save stock adjustment', 'error');
                    $('#btnConfirmSubmit').prop('disabled', false).html('<i class="bi bi-check-circle me-2"></i>Confirm & Submit');
                }
            },
            error: function () {
                showAlert('An error occurred while saving', 'error');
                $('#btnConfirmSubmit').prop('disabled', false).html('<i class="bi bi-check-circle me-2"></i>Confirm & Submit');
            }
        });
    }

    // Show toast notification
    function showAlert(message, type) {
        let toastEl, toastMessage, toastInstance;

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

        // Set message
        toastMessage.textContent = message;

        // Show toast
        toastInstance = new bootstrap.Toast(toastEl, {
            delay: type === 'success' ? 3000 : type === 'warning' ? 4000 : 5000
        });
        toastInstance.show();
    }

})();
