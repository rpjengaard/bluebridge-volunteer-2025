/**
 * Member Modal - Reusable component for displaying member profiles in a popup
 *
 * Usage:
 * 1. Include the _MemberModal partial in your view
 * 2. Call openMemberModal(memberKey) to open the modal
 *
 * Example:
 * <button onclick="openMemberModal('00000000-0000-0000-0000-000000000000')">View Profile</button>
 */

(function() {
    'use strict';

    // Prevent multiple initializations
    if (window.MemberModal) {
        return;
    }

    window.MemberModal = {
        isOpen: false,
        currentMemberKey: null
    };

    /**
     * Opens the member modal and fetches member data
     * @param {string} memberKey - The GUID of the member to display
     */
    window.openMemberModal = function(memberKey) {
        const modal = document.getElementById('memberModal');
        const loading = document.getElementById('modalLoading');
        const error = document.getElementById('modalError');
        const content = document.getElementById('modalContent');

        if (!modal) {
            console.error('Member modal not found. Make sure to include the _MemberModal partial.');
            return;
        }

        // Store current member key
        window.MemberModal.currentMemberKey = memberKey;
        window.MemberModal.isOpen = true;

        // Show modal with loading state
        modal.classList.remove('hidden');
        loading.classList.remove('hidden');
        error.classList.add('hidden');
        content.classList.add('hidden');
        document.body.style.overflow = 'hidden';

        // Fetch member data
        fetch('/api/member/' + memberKey)
            .then(function(response) {
                if (!response.ok) throw new Error('Failed to fetch');
                return response.json();
            })
            .then(function(data) {
                populateMemberModal(data, memberKey);
                loading.classList.add('hidden');
                content.classList.remove('hidden');
            })
            .catch(function(err) {
                console.error('Error fetching member:', err);
                loading.classList.add('hidden');
                error.classList.remove('hidden');
            });
    };

    /**
     * Closes the member modal
     */
    window.closeMemberModal = function() {
        const modal = document.getElementById('memberModal');
        if (modal) {
            modal.classList.add('hidden');
            document.body.style.overflow = '';
            window.MemberModal.isOpen = false;
            window.MemberModal.currentMemberKey = null;
        }
    };

    /**
     * Populates the modal with member data
     * @param {object} data - The member data from the API
     * @param {string} memberKey - The member's GUID
     */
    function populateMemberModal(data, memberKey) {
        // Basic info
        setTextContent('modalMemberName', data.fullName || 'Ukendt');
        setTextContent('modalFirstName', data.firstName || '-');
        setTextContent('modalLastName', data.lastName || '-');

        // Email
        var emailEl = document.getElementById('modalEmail');
        var emailLinkEl = document.getElementById('modalEmailLink');
        if (data.email) {
            emailEl.innerHTML = '<a href="mailto:' + escapeHtml(data.email) + '" class="text-blue-bridge hover:underline">' + escapeHtml(data.email) + '</a>';
            emailLinkEl.href = 'mailto:' + data.email;
        } else {
            emailEl.textContent = '-';
            emailLinkEl.href = '#';
        }

        // Phone
        var phoneEl = document.getElementById('modalPhone');
        var phoneLinkEl = document.getElementById('modalPhoneLink');
        if (data.phone) {
            phoneEl.innerHTML = '<a href="tel:' + escapeHtml(data.phone) + '" class="text-blue-bridge hover:underline">' + escapeHtml(data.phone) + '</a>';
            phoneLinkEl.href = 'tel:' + data.phone;
            phoneLinkEl.classList.remove('hidden');
        } else {
            phoneEl.textContent = '-';
            phoneLinkEl.classList.add('hidden');
        }

        // Age and birthdate
        setTextContent('modalAge', data.age ? data.age + ' år' : '-');
        setTextContent('modalBirthdate', data.birthdate || '-');

        // Member groups
        var groupsEl = document.getElementById('modalMemberGroups');
        groupsEl.innerHTML = '';
        if (data.memberGroups && data.memberGroups.length > 0) {
            data.memberGroups.forEach(function(group) {
                var colorClass = 'bg-green-500';
                var groupLower = group.toLowerCase();
                if (groupLower === 'admin') {
                    colorClass = 'bg-red-500';
                } else if (groupLower === 'vagtplanlæggere') {
                    colorClass = 'bg-purple-500';
                }

                var span = document.createElement('span');
                span.className = colorClass + ' text-white text-xs px-3 py-1 rounded-full';
                span.textContent = group;
                groupsEl.appendChild(span);
            });
        }

        // Previous workplaces
        var workplacesSection = document.getElementById('modalWorkplacesSection');
        var workplacesEl = document.getElementById('modalWorkplaces');
        if (data.tidligereArbejdssteder) {
            workplacesEl.textContent = data.tidligereArbejdssteder;
            workplacesSection.classList.remove('hidden');
        } else {
            workplacesSection.classList.add('hidden');
        }

        // Status
        var statusEl = document.getElementById('modalAcceptStatus');
        if (data.accept2026) {
            statusEl.textContent = 'Bekræftet';
            statusEl.className = 'inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800';
        } else {
            statusEl.textContent = 'Ikke bekræftet';
            statusEl.className = 'inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800';
        }

        // Accepted date
        var acceptedDateRow = document.getElementById('modalAcceptedDateRow');
        if (data.acceptedDate) {
            setTextContent('modalAcceptedDate', data.acceptedDate);
            acceptedDateRow.classList.remove('hidden');
        } else {
            acceptedDateRow.classList.add('hidden');
        }

        // Invitation date
        var invitationDateRow = document.getElementById('modalInvitationDateRow');
        if (data.invitationSentDate) {
            setTextContent('modalInvitationDate', data.invitationSentDate);
            invitationDateRow.classList.remove('hidden');
        } else {
            invitationDateRow.classList.add('hidden');
        }

        // Crew wishes
        var wishesSection = document.getElementById('modalCrewWishesSection');
        var wishesEl = document.getElementById('modalCrewWishes');
        wishesEl.innerHTML = '';
        if (data.crewWishes && data.crewWishes.length > 0) {
            data.crewWishes.forEach(function(crew) {
                var link = document.createElement('a');
                link.href = crew.url || '#';
                link.className = 'inline-flex items-center px-3 py-1 rounded-md text-sm font-medium bg-amber-100 text-amber-800 hover:bg-amber-200 transition-colors';
                link.innerHTML = escapeHtml(crew.name) + '<svg class="ml-1 h-3 w-3" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" d="M13.5 6H5.25A2.25 2.25 0 003 8.25v10.5A2.25 2.25 0 005.25 21h10.5A2.25 2.25 0 0018 18.75V10.5m-10.5 6L21 3m0 0h-5.25M21 3v5.25" /></svg>';
                wishesEl.appendChild(link);
            });
            wishesSection.classList.remove('hidden');
        } else {
            wishesSection.classList.add('hidden');
        }

        // Assigned crews
        var assignedSection = document.getElementById('modalAssignedCrewsSection');
        var assignedEl = document.getElementById('modalAssignedCrews');
        assignedEl.innerHTML = '';
        if (data.assignedCrews && data.assignedCrews.length > 0) {
            data.assignedCrews.forEach(function(crew) {
                var div = document.createElement('div');
                div.className = 'flex items-center justify-between p-2 bg-white rounded';
                var ageLimitHtml = crew.ageLimit ? '<span class="text-xs bg-gray-100 text-gray-600 px-2 py-1 rounded">' + crew.ageLimit + '+ år</span>' : '';
                div.innerHTML = '<a href="' + escapeHtml(crew.url || '#') + '" class="font-medium text-blue-bridge hover:underline">' + escapeHtml(crew.name) + '</a>' + ageLimitHtml;
                assignedEl.appendChild(div);
            });
            assignedSection.classList.remove('hidden');
        } else {
            assignedSection.classList.add('hidden');
        }

        // Full profile link
        document.getElementById('modalFullProfileLink').href = '/member?key=' + memberKey;
    }

    /**
     * Helper function to safely set text content
     */
    function setTextContent(elementId, text) {
        var el = document.getElementById(elementId);
        if (el) {
            el.textContent = text;
        }
    }

    /**
     * Helper function to escape HTML to prevent XSS
     */
    function escapeHtml(text) {
        if (!text) return '';
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // Close modal on escape key
    document.addEventListener('keydown', function(e) {
        if (e.key === 'Escape' && window.MemberModal.isOpen) {
            closeMemberModal();
        }
    });

})();
