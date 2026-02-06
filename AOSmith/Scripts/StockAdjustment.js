// Stock Adjustment Request - JavaScript
(function () {
    'use strict';

    // Global variables
    let gridData = [];
    let nextStockRecSno = 1;

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

        // Submit button
        $('#btnSubmit').on('click', function () {
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
            // REC Type 12: From and To must match
            $('#locationMatchWarning').show();
            
            // Auto-sync To location when From changes
            $('#modalFromLocation').off('change.sync').on('change.sync', function () {
                $('#modalToLocation').val($(this).val());
            });
            
            // Auto-sync From location when To changes
            $('#modalToLocation').off('change.sync').on('change.sync', function () {
                $('#modalFromLocation').val($(this).val());
            });
        } else {
            // REC Type 10: Can be different
            $('#locationMatchWarning').hide();
            $('#modalFromLocation').off('change.sync');
            $('#modalToLocation').off('change.sync');
        }
    }

    // Validate locations based on REC Type
    function validateLocations() {
        const recType = parseInt($('#modalRecType').val());
        const fromLoc = $('#modalFromLocation').val();
        const toLoc = $('#modalToLocation').val();

        if (recType === 12 && fromLoc && toLoc && fromLoc !== toLoc) {
            showAlert('For this adjustment type, From and To locations must be the same', 'warning');
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

    // Submit stock adjustment
    function submitStockAdjustment() {
        if (gridData.length === 0) {
            showAlert('Please add at least one item', 'warning');
            return;
        }

        const data = {
            transactionDate: new Date($('#requestDate').val()),
            lineItems: gridData.map(function (item) {
                return {
                    stockRecSno: item.stockRecSno,
                    recType: item.recType,
                    fromLocation: item.fromLocation,
                    toLocation: item.toLocation,
                    itemCode: item.itemCode,
                    itemDescription: item.itemDescription,
                    qty: item.qty
                };
            })
        };

        $.ajax({
            url: '/StockAdjustment/SaveStockAdjustment',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(data),
            beforeSend: function () {
                $('#btnSubmit').prop('disabled', true).html('<i class="fas fa-spinner fa-spin"></i> Saving...');
            },
            success: function (response) {
                if (response.success) {
                    showAlert('Stock adjustment saved successfully!', 'success');
                    setTimeout(function () {
                        window.location.href = '/Home/Index';
                    }, 1500);
                } else {
                    showAlert(response.message || 'Failed to save stock adjustment', 'error');
                    $('#btnSubmit').prop('disabled', false).html('<i class="fas fa-save"></i> Submit Request');
                }
            },
            error: function () {
                showAlert('An error occurred while saving', 'error');
                $('#btnSubmit').prop('disabled', false).html('<i class="fas fa-save"></i> Submit Request');
            }
        });
    }

    // Show alert message
    function showAlert(message, type) {
        const alertClass = type === 'success' ? 'alert-success' : type === 'warning' ? 'alert-warning' : 'alert-danger';
        const icon = type === 'success' ? 'fa-check-circle' : type === 'warning' ? 'fa-exclamation-triangle' : 'fa-times-circle';
        
        const alertHtml = `
            <div class="alert ${alertClass} alert-dismissible fade show" role="alert">
                <i class="fas ${icon}"></i> ${message}
                <button type="button" class="close" data-dismiss="alert" aria-label="Close">
                    <span aria-hidden="true">&times;</span>
                </button>
            </div>
        `;

        // Remove existing alerts
        $('.alert').remove();

        // Add new alert at top of card body
        $('.card-body').prepend(alertHtml);

        // Auto-dismiss after 5 seconds
        setTimeout(function () {
            $('.alert').alert('close');
        }, 5000);
    }

})();
